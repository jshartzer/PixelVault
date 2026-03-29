# Filename Rules Form Spec

Purpose: define what the Filename Rules screen is for before doing more UI work.

This spec sits beside [FILENAME_PARSING_ARCHITECTURE.txt](/C:/Codex/docs/FILENAME_PARSING_ARCHITECTURE.txt).

The parsing architecture doc defines how rules work.
This doc defines how a human should manage those rules in PixelVault.

## Problem

The current form exposes too much implementation and not enough workflow.

Symptoms:

- users do not know what each button is for
- too many columns are visible at once
- important actions do not put the user into an obvious editing flow
- the screen feels like a raw data inspector instead of a rule-management tool
- regex and parser fields are shown before the user understands the rule they are trying to create

## Product Goal

The screen should help the user do four jobs:

1. understand which built-in filename conventions PixelVault already knows
2. turn an unmatched sample into a usable custom rule
3. disable or override a built-in rule when it is wrong for this library
4. save and verify changes with confidence

If a control does not help with one of those jobs, it probably does not belong in the first version of the form.

## Design Principle

Show intent first, implementation second.

That means:

- readable token rules by default
- sample-driven workflows first
- advanced parser fields only when needed
- one clear editing surface instead of inline editing across a giant grid

## What The Screen Should Not Be

Do not make this a spreadsheet.

Not the primary goal:

- editing every rule field inline in a dense grid
- teaching users regex
- exposing parser internals as the first thing they see
- mixing rule management with generic import troubleshooting

## Primary Workflow

The main happy path should be:

1. user opens Filename Rules
2. user sees recent unmatched samples first
3. user selects a sample
4. user clicks `Create Rule From Sample` or double-clicks the sample
5. PixelVault opens a draft rule editor with a readable token pattern
6. user adjusts platform, title/appid/date meaning if needed
7. user saves the draft
8. user optionally tests the rule against one or more filenames

This is the workflow the screen should optimize for.

## Recommended Layout

Three vertical sections, in this order:

1. `Recent Unmatched Samples`
- top-left or full-width top section
- this is the entry point for new rule creation
- rows should show:
  - filename
  - suggested platform
  - occurrence count
  - last seen
- double-click should always mean `Create Rule From Sample`

2. `Rule Editor`
- the primary editing surface
- only one draft rule is edited at a time
- fields should be simple and readable

3. `Known Rules`
- built-in and custom rules listed below or to the side
- this is for browsing and selecting an existing rule
- not the primary editing surface

## Rule Editor Fields

### Always Visible

- `Rule Name`
- `Enabled`
- `Priority`
- `Rule Pattern`
- `Platform`
- `Tags`
- `When AppID Is Missing`
  - `Do nothing`
  - `Send to manual intake`
- `Preserve file time`

### Rule Pattern Field

This should use readable token syntax by default, for example:

- `[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]`
- `[title]_[yyyy][MM][dd][HH][mm][ss].[ext:media]`
- `clip_[unixms].[ext:video]`
- `[contains:PS5]`

The UI should describe this field as:

- `How the filename is shaped`

Not:

- `Regex pattern`

### Advanced Section

Collapsed by default.

Contains:

- `Convention Id`
- `AppID Group`
- `Title Group`
- `Timestamp Group`
- `Timestamp Format`
- raw regex preview if useful

These are important, but not first-class for most edits.

## Buttons And Exact Behavior

### `New Rule`

Expected behavior:

- create a blank draft in the Rule Editor
- focus the `Rule Name` or `Rule Pattern` field immediately
- do not require the user to hunt for where the new row appeared

### `Create Rule From Sample`

Enabled when:

- exactly one sample is selected

Expected behavior:

- create a draft in the Rule Editor using the selected sample
- prefill:
  - rule name
  - readable token pattern
  - suggested platform
  - timestamp format if detected
- keep the original sample visible so the user can compare against it

### `Promote Frequent`

Expected behavior:

- open a small chooser listing repeated unmatched patterns
- user checks one or more samples to promote
- PixelVault creates one draft per selected sample or one grouped draft if the pattern shape is the same

Do not make this a silent bulk action.

### `Disable Built-In`

Enabled when:

- one built-in rule is selected

Expected behavior:

- create a custom override draft for the selected built-in
- mark it disabled
- explain in plain language:
  - `This does not delete the built-in rule. It saves a custom override for this library.`

### `Save Rules`

Expected behavior:

- validate drafts
- persist custom rules
- invalidate parser cache
- keep the user on the same selected rule
- show a short success message

### `Reload`

Expected behavior:

- discard unsaved edits after confirmation
- reload custom rules and samples from disk

### `Close`

Expected behavior:

- if dirty, warn before closing

## Row Click Behavior

### Sample Row Single Click

- populate a small sample detail area or preview text
- enable `Create Rule From Sample`

### Sample Row Double Click

- same as `Create Rule From Sample`

### Built-In Rule Single Click

- show the built-in rule details in read-only mode
- enable `Disable Built-In`

### Custom Rule Single Click

- load that rule into the Rule Editor for editing

### Custom Rule Double Click

- same as single click, but put focus into the editor

## Known Rules List

Keep the list compact.

Columns for first version:

- Name
- On
- Priority
- Platform
- Pattern summary

Do not show every parser field in the list.
Those belong in the editor.

## Validation

Validation should be plain English.

Examples:

- `Rule pattern is required.`
- `This token pattern is not valid. Unknown token: [month].`
- `Timestamp format is required when the rule captures a timestamp.`
- `Platform is required for this rule.`

Avoid raw regex exceptions unless the user is explicitly in advanced mode.

## Suggested First Version

Build the form in two levels:

### Version 1

- sample list
- built-in/custom rule list
- single rule editor
- `New Rule`
- `Create Rule From Sample`
- `Disable Built-In`
- `Save`
- `Reload`
- `Close`

### Version 2

- grouped `Promote Frequent`
- inline filename test box
- raw regex preview
- export/import rules

## Accessibility And Clarity Rules

- every button must have a visible enabled/disabled state tied to selection
- every button should have a tooltip
- selection should visibly change the active panel
- after any action, focus should move to the next obvious place
- no action should silently add hidden data without surfacing it in the editor

## Success Criteria

The screen is working when:

1. a new user can understand the purpose of each section in under 10 seconds
2. a user can turn one unmatched sample into one saved rule without guessing
3. a built-in rule can be disabled without understanding the DB model
4. the list of rules is readable without knowing regex
5. there is one obvious place to edit a rule

## Recommended Next Implementation Order

1. simplify the screen around a single Rule Editor panel
2. reduce the rules grids to compact summaries
3. wire sample selection -> draft editor flow cleanly
4. move advanced parser fields behind an expandable section
5. only then revisit bulk promotion and test tooling
