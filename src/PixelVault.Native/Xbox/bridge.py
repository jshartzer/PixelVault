"""PixelVault's private JSON bridge to OpenXbox. Tokens never cross stdout."""
import asyncio
import ctypes
from ctypes import wintypes
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
import math
import os
from pathlib import Path
import secrets
import sys
import time
from urllib.parse import parse_qs, urlparse
import webbrowser


def protect(data, decrypt=False):
    class Blob(ctypes.Structure):
        _fields_ = [("size", wintypes.DWORD), ("data", ctypes.POINTER(ctypes.c_ubyte))]
    buffer = ctypes.create_string_buffer(data)
    source = Blob(len(data), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte)))
    target = Blob()
    dll = ctypes.WinDLL("crypt32", use_last_error=True)
    call = dll.CryptUnprotectData if decrypt else dll.CryptProtectData
    call.argtypes = [ctypes.POINTER(Blob), ctypes.c_void_p, ctypes.c_void_p,
                     ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(Blob)]
    call.restype = wintypes.BOOL
    if not call(ctypes.byref(source), None, None, None, None, 1, ctypes.byref(target)):
        raise RuntimeError("Windows could not access the saved Xbox sign-in. Reconnect your account.")
    try:
        return ctypes.string_at(target.data, target.size)
    finally:
        kernel = ctypes.WinDLL("kernel32")
        kernel.LocalFree.argtypes = [ctypes.c_void_p]
        kernel.LocalFree.restype = ctypes.c_void_p
        kernel.LocalFree(target.data)


def save_tokens(path, auth):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix("." + secrets.token_hex(8) + ".tmp")
    try:
        temporary.write_bytes(protect(auth.oauth.model_dump_json().encode()))
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def login_code(auth):
    state = secrets.token_urlsafe(32)
    result = {}

    class Callback(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass  # Callback URLs contain credentials.

        def do_GET(self):
            url = urlparse(self.path)
            query = parse_qs(url.query)
            if url.path != "/auth/callback" or query.get("state") != [state]:
                self.send_error(400, "Invalid sign-in callback")
                return
            result["code"] = query.get("code", [None])[0]
            body = b"You can return to PixelVault." if result["code"] else b"Sign-in was cancelled. Return to PixelVault."
            self.send_response(200)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

    # Upstream's registered desktop redirect uses localhost:8080.
    with HTTPServer(("127.0.0.1", 8080), Callback) as server:
        server.timeout = 1
        webbrowser.open(auth.generate_authorization_url(state=state))
        deadline = time.monotonic() + 240
        while not result and time.monotonic() < deadline:
            server.handle_request()
    if not result.get("code"):
        raise RuntimeError("Xbox sign-in was cancelled or timed out. Select Connect to try again.")
    return result["code"]


async def pages(client, url, version, key, params=None):
    collected, seen = [], set()
    params = dict(params or {})
    for _ in range(500):
        response = await client.session.get(url, headers={"x-xbl-contract-version": str(version)}, params=params)
        response.raise_for_status()
        data = response.json()
        if not isinstance(data.get(key), list):
            raise RuntimeError("Xbox returned an incomplete response. Saved progress was retained.")
        collected.extend(data[key])
        token = (data.get("pagingInfo") or {}).get("continuationToken")
        if not token:
            return collected
        if token in seen:
            raise RuntimeError("Xbox repeated a page. Saved progress was retained.")
        seen.add(token)
        params["continuationToken"] = token
        await asyncio.sleep(0.25)
    raise RuntimeError("Xbox returned too many pages. Saved progress was retained.")


def ticks(value):
    try:
        date = datetime.fromisoformat(value.replace("Z", "+00:00"))
        if date.tzinfo is None:
            date = date.replace(tzinfo=timezone.utc)
        if datetime(1970, 1, 1, tzinfo=timezone.utc) <= date <= datetime.now(timezone.utc):
            delta = date - datetime(1, 1, 1, tzinfo=timezone.utc)
            return (delta.days * 86400 + delta.seconds) * 10000000 + delta.microseconds * 10
    except (ValueError, TypeError, AttributeError):
        pass
    return 0


def number(value):
    try:
        result = float(value)
        return result if math.isfinite(result) else None
    except (ValueError, TypeError):
        return None


def sort_key(value, fallback):
    # Legacy Xbox titles can return UInt32.MaxValue for an unspecified sequence.
    # SortKey is a signed Int32 in PixelVault; preserve response order for unusable values.
    if isinstance(value, str) and value.isascii() and value.isdecimal() and len(value) <= 10:
        value = int(value)
    return value if type(value) is int and 0 <= value <= 2147483647 else fallback


def merge_legacy(definitions, earned):
    by_id = {str(item["id"]): item for item in definitions}
    earned_by_id = {str(item["id"]): item for item in earned}
    if len(by_id) != len(definitions) or len(earned_by_id) != len(earned) or not earned_by_id.keys() <= by_id.keys():
        raise RuntimeError("Xbox 360 returned inconsistent achievement identities.")
    return [dict(item, **({**earned_by_id[key], "unlocked": True} if key in earned_by_id
                          else {"unlocked": False})) for key, item in by_id.items()]


def rows(achievements, title_id, legacy):
    result, identities = [], set()
    for item in achievements:
        identity = str(item.get("id", ""))
        if not identity or identity in identities:
            raise RuntimeError("Xbox returned missing or duplicate achievement identities.")
        identities.add(identity)
        if legacy:
            if str(item.get("titleId")) != title_id or not isinstance(item.get("unlocked"), bool):
                raise RuntimeError("Xbox returned mismatched or unknown achievement progress.")
            known, unlocked = True, item["unlocked"] and not item.get("isRevoked", False)
            score = item.get("gamerscore")
        else:
            if not any(str(t.get("id")) == title_id for t in item.get("titleAssociations", [])):
                raise RuntimeError("Xbox returned achievements for another title.")
            state = item.get("progressState")
            known = state in ("Achieved", "NotStarted", "InProgress")
            unlocked = state == "Achieved" and not item.get("isRevoked", False)
            score = next((r.get("value") for r in item.get("rewards", []) if r.get("type") == "Gamerscore"), None)
        progression = item.get("progression") or {}
        requirements = progression.get("requirements") or []
        requirement = requirements[0] if len(requirements) == 1 else {}
        rarity = number((item.get("rarity") or {}).get("currentCategoryPercentage"))
        icon = next((a.get("url") for a in item.get("mediaAssets", []) if a.get("type") == "Icon"), None)
        if legacy:
            icon = legacy_icon(title_id, item.get("imageId"))
        if icon and not icon.startswith("https://"):
            icon = None
        result.append(dict(Provider="xbox", ProviderGameId=title_id, ProviderAchievementId=identity,
                           Title=item.get("name", identity), Description=item.get("description") if unlocked else item.get("lockedDescription", item.get("description")),
                           Meta=(str(score) + " Gamerscore") if score is not None else "Xbox",
                           SortKey=sort_key(item.get("sequence"), len(result)), ProgressKnown=known, Unlocked=unlocked,
                           UnlockUtcTicks=ticks(item.get("timeUnlocked") if legacy else progression.get("timeUnlocked")) if unlocked else 0,
                           Hidden=item.get("isSecret", False), IconUrlColor=icon,
                           GlobalUnlockPercent=rarity if rarity is not None and 0 <= rarity <= 100 else None,
                           CounterCurrent=number(requirement.get("current")), CounterTarget=number(requirement.get("target"))))
    return result


def legacy_icon(title_id, image_id):
    # Xbox 360 returns an image ID rather than a mediaAssets URL.
    def unsigned(value):
        if isinstance(value, str) and value.isascii() and value.isdecimal() and len(value) <= 10:
            value = int(value)
        return value if type(value) is int and 0 <= value <= 4294967295 else None
    title, image = unsigned(title_id), unsigned(image_id)
    if not title or image is None:
        return None
    return f"https://image-ssl.xboxlive.com/global/t.{title:08x}/ach/0/{image:x}"


def title_rows(modern, legacy, history):
    played = {}
    names = {}
    same_names = {}
    name_key = lambda name: "".join(c for c in name.casefold() if c.isalnum())
    for title in [*modern, *legacy]:
        same_names.setdefault(name_key(title["name"]), set()).add(str(title["titleId"]))
    for title in history:
        value = ticks((title.get("titleHistory") or {}).get("lastTimePlayed"))
        identity = str(title.get("titleId", ""))
        name = title.get("name")
        if isinstance(name, str) and name.strip():
            names[identity] = name.strip()
        if value:
            played[identity] = max(played.get(identity, 0), value)
    result = {}
    for old, items in ((False, modern), (True, legacy)):
        for title in items:
            identity = str(title["titleId"])
            # lastUnlock is achievement activity, not last played.
            last = max(played.get(identity, 0), ticks(title.get("lastPlayed")) if old else 0)
            # Achievement history can call Xbox, Windows and iOS releases simply "Minecraft".
            # Use the richer title history to distinguish collisions; retain unique existing names.
            name = names.get(identity, title["name"]) if len(same_names[name_key(title["name"])]) > 1 else title["name"]
            result[(identity, old)] = dict(Id=identity, Name=name, Legacy=old, LastPlayedUtc=last)
    return sorted(result.values(), key=lambda t: t["Name"].casefold())


async def title_history(client, xuid):
    url = f"https://titlehub.xboxlive.com/users/xuid({xuid})/titles/titlehistory/decoration/achievement"
    # The title-history service returns the requested complete history in one response.
    response = await client.session.get(url, params={"maxItems": 10000}, headers=client.titlehub._headers)
    response.raise_for_status()
    data = response.json()
    if str(data.get("xuid")) != xuid or not isinstance(data.get("titles"), list):
        raise RuntimeError("Xbox returned invalid title history. Saved progress was retained.")
    return data["titles"]


async def run(request):
    root = Path(os.environ["LOCALAPPDATA"]) / "PixelVault" / "Xbox"
    token_path = root / "tokens.dpapi"
    command = request["command"]
    if command == "disconnect":
        token_path.unlink(missing_ok=True)
        return {"ok": True}
    if command == "status":
        return {"connected": token_path.exists()}
    from xbox.webapi.api.client import XboxLiveClient
    from xbox.webapi.authentication.manager import AuthenticationManager
    from xbox.webapi.authentication.models import OAuth2TokenResponse
    from xbox.webapi.common.signed_session import SignedSession
    from xbox.webapi.scripts import CLIENT_ID, CLIENT_SECRET, REDIRECT_URI
    async with SignedSession() as session:
        auth = AuthenticationManager(session, CLIENT_ID, CLIENT_SECRET, REDIRECT_URI)
        if command == "connect":
            await auth.request_tokens(await asyncio.to_thread(login_code, auth))
        else:
            if not token_path.exists():
                raise RuntimeError("Connect your Xbox account first.")
            auth.oauth = OAuth2TokenResponse.model_validate_json(protect(token_path.read_bytes(), decrypt=True))
            await auth.refresh_tokens()
        save_tokens(token_path, auth)
        client = XboxLiveClient(auth)
        xuid = str(client.xuid)
        expected = request.get("xuid")
        if expected and xuid != expected:
            raise RuntimeError("The Xbox account changed. Reload your games before continuing.")
        base = f"https://achievements.xboxlive.com/users/xuid({xuid})"
        if command == "connect":
            return {"xuid": xuid}
        if command == "titles":
            modern = await pages(client, base + "/history/titles", 2, "titles")
            legacy = await pages(client, base + "/history/titles", 1, "titles")
            history = await title_history(client, xuid)
            return {"xuid": xuid, "titles": title_rows(modern, legacy, history)}
        if command == "achievements":
            title_id = str(request["titleId"])
            if not title_id.isdecimal():
                raise RuntimeError("Invalid Xbox title ID.")
            legacy = request.get("legacy", False)
            endpoint = "/titleachievements" if legacy else "/achievements"
            items = await pages(client, base + endpoint, 1 if legacy else 2, "achievements", {"titleId": title_id})
            if legacy:
                earned = await pages(client, base + "/achievements", 1, "achievements", {"titleId": title_id})
                items = merge_legacy(items, earned)
            return {"xuid": xuid, "rows": rows(items, title_id, legacy)}
        raise RuntimeError("Unsupported Xbox command.")


if __name__ == "__main__":
    try:
        request = json.loads(sys.stdin.readline())
        # Upstream may print authentication diagnostics; stdout is protocol-only.
        import contextlib
        with contextlib.redirect_stdout(sys.stderr):
            response = asyncio.run(run(request))
        print(json.dumps(response, allow_nan=False))
    except Exception as error:
        # Never emit HTTP URLs, bodies, credentials, or provider exception strings.
        message = str(error) if type(error) is RuntimeError else "Xbox request failed. Check your connection and reconnect if necessary."
        if type(error).__name__ == "HTTPStatusError":
            status = error.response.status_code
            message = f"Xbox returned HTTP {status}. " + ("Try again later." if status == 429 else "Reconnect your account and check its Xbox privacy settings.")
        print(json.dumps({"error": message}))
        sys.exit(1)
