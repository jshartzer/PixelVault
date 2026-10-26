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
        items.append({"Id": row["ResourceUUID"], "Name": plain(row["TitleText"]),
                      "Category": groups.get(row["Group"]["name"], row["Group"]["name"]),
                      "Detail": "\n".join(v for v in metadata if v) + "\n\nIndividual read status is manual; the linked automatic counter tracks the achievement total.",
                      "Content": body, "Target": 1, "Order": order,
                      "Identifiers": {"ResourceUUID": row["ResourceUUID"], "Language": "English", "ContentSource": "Installed game: ds/archives/interviews_resource.core"}})
    if not items or sum(len(i["Content"]) for i in items) > 2000000:
        raise ValueError("Empty or oversized catalogue")
    return {"Id": "death-stranding-dc.interview-catalogue", "Provider": "steam", "GameId": "1850570",
            "GameTitle": "Death Stranding Director's Cut", "AchievementId": "039", "Title": "Interview catalogue",
            "Description": "Installed English interview reference library. Content is saved locally. Opening it does not mark interviews read in the game. Individual checkboxes are manual; use Interviews — The Past Guides the Present for the automatic 100-interview achievement counter.",
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
