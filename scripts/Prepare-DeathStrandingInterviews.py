"""Build a private tracker definition from a read-only, English Decima catalogue export.

Usage: python Prepare-DeathStrandingInterviews.py interviews.json output-directory
Input is produced by InterviewContentCatalogue.java using Decima Workshop.
Neither extracted prose nor generated definitions belong in source control.
"""
import collections
import hashlib
import html
import json
from pathlib import Path
import re
import sys


MISSABLE_INTERVIEWS = {
    "e13f55c5-4b95-8f41-a8ec-65be8acf1293":
        "Reach three stars with Lake Knot City by completing Order 23 (Retrieval: System Server). "
        "Players report missing this interview if Lake Knot City is already at three stars."
}


def plain(value):
    value = re.sub(r"<br\s*/?>", "\n", value or "", flags=re.I)
    value = re.sub(r"</?color(?:\s+[^>]*)?>", "", value, flags=re.I)
    return html.unescape(value).replace("\r\n", "\n").strip()


def prepare(rows):
    groups = {"Diehardman": "Die-Hardman", "OldDiehardman": "Die-Hardman (early records)",
              "BridgesStaff": "Bridges Staff", "HiggesNote": "Higgs's Journals", "LucyReport": "Lucy's Reports"}
    usable = [r for r in rows if r.get("TitleText", "").strip() and r.get("DescriptionText", "").strip()]
    identities = [r["ResourceUUID"] for r in usable]
    if len(identities) != len(set(identities)):
        raise ValueError("Duplicate interview resource UUIDs")
    items = []
    for order, row in enumerate(sorted(usable, key=lambda r: (r["Group"]["value"], r["MenuDisplayPriority"], r["ResourceUUID"]))):
        body = plain(row["DescriptionText"])
        if not body or len(body) > 100000:
            raise ValueError("Invalid body length")
        metadata = [plain(row.get(k)) for k in ("WhoText", "WhenText", "WhereText")]
        identity = row["ResourceUUID"]
        unlock_hint = MISSABLE_INTERVIEWS.get(identity)
        detail = "\n".join(v for v in metadata if v)
        if unlock_hint:
            detail = "\n\n".join(v for v in (detail, unlock_hint) if v)
        items.append({"Id": identity, "Name": plain(row["TitleText"]),
                      "Category": groups.get(row["Group"]["name"], row["Group"]["name"]),
                      "Detail": detail, "Missable": bool(unlock_hint),
                      "Content": body, "Target": 1, "Order": order,
                      "Identifiers": {"ResourceUUID": row["ResourceUUID"], "Language": "English", "ContentSource": "Installed game: ds/archives/interviews_resource.core"}})
    if not items or sum(len(i["Content"]) for i in items) > 2000000:
        raise ValueError("Empty or oversized catalogue")
    return {"Id": "death-stranding-dc.interview-catalogue", "Provider": "steam", "GameId": "1850570",
            "GameTitle": "Death Stranding Director's Cut", "AchievementId": "039", "Title": "Interview catalogue",
            "Description": "Interviews from the in-game Data menu.",
            "CompletionLabel": "Marked read", "Milestones": [], "CategoryTargets": {}, "Items": items}


if __name__ == "__main__":
    source, destination = Path(sys.argv[1]), Path(sys.argv[2])
    rows = json.loads(source.read_text(encoding="utf-8"))
    definition = prepare(rows)
    destination.mkdir(parents=True, exist_ok=True)
    encoded = json.dumps(definition, ensure_ascii=False, indent=2).encode("utf-8")
    (destination / "tracker-definition.json").write_bytes(encoded)
    evidence = {"Resources": len(rows), "UsableInterviews": len(definition["Items"]),
                "EmptyResourcesExcluded": len(rows) - len(definition["Items"]),
                "Categories": dict(collections.Counter(i["Category"] for i in definition["Items"])),
                "DefinitionSha256": hashlib.sha256(encoded).hexdigest(),
                "ContentCharacters": sum(len(i["Content"]) for i in definition["Items"])}
    (destination / "catalogue-evidence.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    print(json.dumps(evidence))
