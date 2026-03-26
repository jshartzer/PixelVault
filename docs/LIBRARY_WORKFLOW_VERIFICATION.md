# Library Workflow Verification

Use this checklist when touching Library regrouping, deletion, comment editing, or thumbnail/cache invalidation behavior.

## Seed Data

Generate a disposable verification dataset with:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Codex\scripts\New-LibraryWorkflowVerificationData.ps1
```

This creates:

- `C:\Codex\tmp_verify\library-workflows\library`
- `C:\Codex\tmp_verify\library-workflows\source`

Point PixelVault at those paths in `Path Settings` before running the checks.

## Regroup Into Existing Folder

1. Open Library and select two files in `Alpha Quest`.
2. Use `Edit Metadata`.
3. Change the game title to `Beta Zone` and set platform to `PS5`.
4. Save.

Expected:

- the selected files move into the existing `Beta Zone` group
- no duplicate orphan `Beta Zone` folder tile appears
- unaffected folder tiles keep their thumbnails instead of blanking
- reopening `Edit Metadata` on the moved files shows the updated title/platform

## Regroup Into New Folder

1. Select one remaining file in `Alpha Quest`.
2. Use `Edit Metadata`.
3. Change the game title to `Omega Shift` and choose a different platform than the source file.
4. Save.

Expected:

- a new folder tile appears for `Omega Shift`
- the edited file leaves the old folder
- unrelated folders remain visible and keep their thumbnails

## Comment Clear

1. Open `Edit Metadata` on a file that already has a comment, such as one in `Beta Zone`.
2. Confirm the comment box is prefilled.
3. Clear the comment box completely and save.
4. Reopen `Edit Metadata` on the same file.

Expected:

- the comment box stays blank after reopening
- the old embedded comment does not come back

## Partial Delete

1. Open a folder with multiple captures, such as `Gamma Trials`.
2. Select one capture and delete it from the Library detail view.

Expected:

- only the selected file is removed
- the folder still exists with the remaining capture(s)
- nearby folder thumbnails remain populated

## Delete Last File In Folder

1. Delete the remaining file(s) from a small folder until it becomes empty.

Expected:

- the empty folder disappears from the Library
- other folder tiles remain populated
- no broad thumbnail flush is visible outside the affected folder

## Manual Spot Check

After the checks above:

- run `Refresh`
- run `Rebuild`
- confirm the same folders and capture counts still make sense
- confirm `Edit Metadata` still opens correctly on a multi-selection
