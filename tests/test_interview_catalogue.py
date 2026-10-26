"""Synthetic extractor-transform tests; no proprietary prose or save data."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("prepare_interviews", Path(__file__).parents[1] / "scripts/Prepare-DeathStrandingInterviews.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CatalogueTests(unittest.TestCase):
    def row(self, identity="uuid-1"):
        return {"ResourceUUID": identity, "Group": {"name": "BridgesStaff", "value": 7}, "MenuDisplayPriority": 5,
                "TitleText": "Reference", "DescriptionText": "One<br><color name=yellow>Two</color> &amp; three", "WhoText": "Author", "WhenText": "Date", "WhereText": "Place"}

    def test_identity_metadata_body_and_empty_resources(self):
        empty = self.row("empty");empty["TitleText"] = "";empty["DescriptionText"] = ""
        d = module.prepare([self.row(), empty])
        self.assertEqual("039", d["AchievementId"])
        self.assertEqual(1, len(d["Items"]))
        row = d["Items"][0]
        self.assertEqual("uuid-1", row["Id"])
        self.assertEqual("Bridges Staff", row["Category"])
        self.assertEqual("One\nTwo & three", row["Content"])
        self.assertIn("Author\nDate\nPlace", row["Detail"])
        self.assertNotIn("Current", row)

    def test_duplicate_identity_and_oversized_text_fail(self):
        with self.assertRaises(ValueError): module.prepare([self.row(), self.row()])
        row = self.row();row["DescriptionText"] = "a" * 100001
        with self.assertRaises(ValueError): module.prepare([row])


if __name__ == "__main__": unittest.main()
