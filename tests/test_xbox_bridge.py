import asyncio
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("xbox_bridge", Path(__file__).parents[1] / "src/PixelVault.Native/Xbox/bridge.py")
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)


def achievement(**changes):
    data = dict(id="1", titleAssociations=[{"id": 123}], name="First steps", progressState="Achieved",
                progression={"timeUnlocked": "2025-01-02T03:04:05Z", "requirements": [{"current": "2", "target": "10"}]},
                rewards=[{"type": "Gamerscore", "value": "15"}], mediaAssets=[{"type": "Icon", "url": "https://example.org/a.png"}])
    data.update(changes)
    return data


class BridgeTests(unittest.TestCase):
    def test_360_icons_use_title_and_image_ids_not_achievement_sequence(self):
        item = dict(id=1, titleId=1480657456, name="Legacy", unlocked=True, imageId=31)
        row = bridge.rows([item], "1480657456", True)[0]
        self.assertEqual(row["IconUrlColor"], "https://image-ssl.xboxlive.com/global/t.58410a30/ach/0/1f")
        for value in (None, -1, 4294967296, True, "../image", "0x20"):
            self.assertIsNone(bridge.legacy_icon("1480657456", value))
        self.assertIsNone(bridge.legacy_icon("0", 1))

    def test_play_dates_use_history_not_unlocks_and_preserve_legacy_fallback(self):
        modern = [dict(titleId=1, name="Modern", lastUnlock="2025-12-01T00:00:00Z"), dict(titleId=2, name="Unknown", lastUnlock="2025-12-01T00:00:00Z")]
        legacy = [dict(titleId=3, name="Legacy", lastPlayed="2025-01-01T00:00:00Z")]
        history = [dict(titleId="1", titleHistory={"lastTimePlayed":"2025-02-03T00:00:00Z"}), dict(titleId="2", titleHistory={"lastTimePlayed":"9999-01-01T00:00:00Z"})]
        titles = {t["Id"]:t for t in bridge.title_rows(modern, legacy, history)}
        self.assertEqual(titles["1"]["LastPlayedUtc"], bridge.ticks("2025-02-03T00:00:00Z"))
        self.assertEqual(titles["2"]["LastPlayedUtc"], 0)
        self.assertEqual(titles["3"]["LastPlayedUtc"], bridge.ticks("2025-01-01T00:00:00Z"))
        self.assertTrue(titles["3"]["Legacy"])

    def test_sort_order_is_always_a_signed_int32(self):
        for value, expected in [(4294967295, 3), (2147483648, 3), (None, 3), (-1, 3),
                                (True, 3), (1.5, 3), ("unknown", 3), ("4294967295", 3),
                                (0, 0), (2147483647, 2147483647), ("12", 12)]:
            with self.subTest(value=value):
                self.assertEqual(bridge.sort_key(value, 3), expected)
                self.assertIs(type(bridge.sort_key(value, 3)), int)

    def test_legacy_unknown_sequence_preserves_list_order_and_earned_state(self):
        definitions = [dict(id=i, titleId=123, name=str(i), sequence=4294967295, unlocked=False) for i in (5, 2, 9)]
        earned = [dict(id=2, titleId=123, sequence=4294967295, unlocked=True)]
        rows = bridge.rows(bridge.merge_legacy(definitions, earned), "123", True)
        self.assertEqual([r["SortKey"] for r in rows], [0, 1, 2])
        self.assertEqual([r["ProviderAchievementId"] for r in rows], ["5", "2", "9"])
        self.assertEqual([r["Unlocked"] for r in rows], [False, True, False])

    def test_maps_progress_without_awarding_pixelvault_points(self):
        row = bridge.rows([achievement()], "123", False)[0]
        self.assertTrue(row["Unlocked"])
        self.assertTrue(row["ProgressKnown"])
        self.assertGreater(row["UnlockUtcTicks"], 0)
        self.assertEqual(row["CounterCurrent"], 2)
        self.assertEqual(row["Meta"], "15 Gamerscore")
        self.assertNotIn("Points", row)

    def test_unknown_progress_is_not_locked(self):
        row = bridge.rows([achievement(progressState="Unknown")], "123", False)[0]
        self.assertFalse(row["ProgressKnown"])
        self.assertEqual(row["UnlockUtcTicks"], 0)

    def test_rejects_mismatch_and_duplicates(self):
        with self.assertRaises(RuntimeError):
            bridge.rows([achievement()], "456", False)
        with self.assertRaises(RuntimeError):
            bridge.rows([achievement(), achievement()], "123", False)

    def test_invalid_dates_and_nonfinite_counters(self):
        self.assertEqual(bridge.ticks("0001-01-01T00:00:00Z"), 0)
        self.assertEqual(bridge.ticks("9999-01-01T00:00:00Z"), 0)
        self.assertIsNone(bridge.number("NaN"))
        self.assertIsNone(bridge.number("Infinity"))

    def test_360_revoked_and_invalid_identity(self):
        item = dict(id=1, titleId=123, name="Legacy", unlocked=True, isRevoked=True, gamerscore=10)
        self.assertFalse(bridge.rows([item], "123", True)[0]["Unlocked"])
        with self.assertRaises(RuntimeError):
            bridge.rows([item], "456", True)

    def test_360_merges_earned_endpoint_into_catalogue(self):
        definitions = [dict(id=1, titleId=123, name="Earned", unlocked=False), dict(id=2, titleId=123, name="Locked", unlocked=False)]
        earned = [dict(id=1, titleId=123, unlocked=True, timeUnlocked="2025-01-02T03:04:05Z")]
        rows = bridge.rows(bridge.merge_legacy(definitions, earned), "123", True)
        self.assertTrue(rows[0]["Unlocked"])
        self.assertGreater(rows[0]["UnlockUtcTicks"], 0)
        self.assertFalse(rows[1]["Unlocked"])
        with self.assertRaises(RuntimeError):
            bridge.merge_legacy(definitions, [dict(id=3)])

    def test_windows_token_encryption_roundtrip(self):
        original = b'{"refresh_token":"synthetic-test-token"}'
        encrypted = bridge.protect(original)
        self.assertNotIn(b"synthetic-test-token", encrypted)
        self.assertEqual(bridge.protect(encrypted, decrypt=True), original)


class TitleIdentityTests(unittest.TestCase):
    def test_richer_names_distinguish_platform_releases_without_renaming_unique_titles(self):
        modern=[dict(titleId=i,name="Minecraft") for i in (1,2,3)] + [dict(titleId=5,name="Unique saved name")]
        legacy=[dict(titleId=4,name="Minecraft")]
        history=[dict(titleId=i,name=n) for i,n in [(1,"Minecraft"),(2,"Minecraft for Windows"),(3,"Minecraft for iOS"),(4,"Minecraft: Xbox 360 Edition"),(5,"Another catalogue name")]]
        titles={t["Id"]:t for t in bridge.title_rows(modern,legacy,history)}
        self.assertEqual([titles[str(i)]["Name"] for i in range(1,6)],["Minecraft","Minecraft for Windows","Minecraft for iOS","Minecraft: Xbox 360 Edition","Unique saved name"])
        self.assertTrue(titles["4"]["Legacy"])
        self.assertEqual(titles["2"]["LastPlayedUtc"],0)

    def test_missing_or_blank_history_names_preserve_saved_titles(self):
        titles=bridge.title_rows([dict(titleId=1,name="Example"),dict(titleId=2,name="Example")],[],[dict(titleId=1,name=" ")])
        self.assertEqual([t["Name"] for t in titles],["Example","Example"])


class PageTests(unittest.IsolatedAsyncioTestCase):
    async def fetch(self, responses):
        class Response:
            def __init__(self, data): self.data = data
            def raise_for_status(self): pass
            def json(self): return self.data
        class Client:
            def __init__(self): self.session = self; self.requests = []
            async def get(self, url, **kwargs):
                self.requests.append(dict(kwargs["params"]))
                return Response(responses[len(self.requests) - 1])
        client = Client()
        result = await bridge.pages(client, "https://example.org", 2, "achievements")
        return result, client.requests

    async def test_collects_every_page(self):
        result, requests = await self.fetch([
            {"achievements": [1], "pagingInfo": {"continuationToken": "next"}},
            {"achievements": [2], "pagingInfo": {"continuationToken": None}}])
        self.assertEqual(result, [1, 2])
        self.assertEqual(requests[1]["continuationToken"], "next")

    async def test_repeated_page_and_malformed_response_fail(self):
        with self.assertRaises(RuntimeError):
            await self.fetch([{"achievements": [], "pagingInfo": {"continuationToken": "loop"}}] * 2)
        with self.assertRaises(RuntimeError):
            await self.fetch([{"error": "partial"}])


if __name__ == "__main__":
    unittest.main()
