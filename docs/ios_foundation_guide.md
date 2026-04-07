# iOS Foundation Guide

## Purpose

PixelVault will remain a full Windows desktop management app.
A future iOS app will be a separate project, built specifically for mobile use.
The iOS app should primarily be a browse and viewing experience, with limited lightweight write actions such as stars and comments.
The iOS app should connect to a self-hosted PixelVault backend or service layer rather than directly to raw library files.

## Product Direction

### Desktop owns
- import workflows
- heavy metadata operations
- cover management
- external ID management
- library maintenance and repair
- file moves, renames, and destructive operations
- direct integrations with ExifTool, FFmpeg, and local filesystem concerns

### iOS owns
- browse library
- search and filter
- view screenshots and clips
- view game and capture metadata
- star and unstar captures or games
- add and edit comments
- other lightweight user-facing interactions that do not depend on desktop-only workflows

## Core Architectural Intent

The future mobile app should not be treated as a smaller version of the Windows desktop app.
It should be treated as a separate client that consumes stable application data from a backend.

The long-term shape is:
- **PixelVault Desktop** = full management tool
- **PixelVault backend/service** = source of truth for mobile-safe data and actions
- **PixelVault iOS** = browse and interact client

## Architectural Guardrails

When adding new features, prefer:
- service-layer logic over `MainWindow` logic
- plain data models over WPF-bound objects
- backend-friendly request/response shapes over UI-owned state
- async service calls over tightly coupled UI workflows
- reusable domain logic that can later be exposed by an API

Avoid:
- putting reusable rules directly in `MainWindow`
- tying business logic to `Dispatcher`, `Window`, `MessageBox`, or `Forms`
- embedding Windows path assumptions into core logic
- mixing filesystem operations into UI event handlers when a service seam is possible
- making future mobile-relevant behavior depend on WPF execution

## Data That Should Stay Client-Agnostic

The following kinds of models should eventually be usable by:
1. the Windows desktop app
2. a future self-hosted backend
3. a future iOS client

Examples:
- `GameSummary`
- `GameDetail`
- `CaptureSummary`
- `CaptureDetail`
- `CommentRecord`
- `StarState`
- `LibraryQuery`
- `LibraryFilter`
- `LibrarySort`
- `LibraryQueryResult`

These models should be plain and serializable, without dependence on WPF types or desktop-only UI behavior.

## Mobile-Safe Writes

The future iOS app may support:
- star and unstar
- add or edit comment
- lightweight personal flags such as favorite or showcase if that fits the product later

The future iOS app should not directly own:
- imports
- file moves or renames
- destructive maintenance
- cover download or cover repair workflows
- Steam or SteamGridDB ID lookup and editing
- metadata rebuild jobs
- repair or migration operations

## Backend-Oriented Thinking

Future mobile access should assume:
- the backend owns indexing and query behavior
- the backend exposes thumbnails, posters, and playable media URLs
- the backend stores and returns stars, comments, and similar lightweight state
- the backend mediates any write action allowed from mobile
- the iOS app consumes stable contracts rather than filesystem structure

This means new desktop work should move toward shapes that can later become backend endpoints.

## What To Prefer In New Desktop Work

Prefer code that:
- can be expressed as a service
- returns plain models
- has clear inputs and outputs
- can later be wrapped by an API without major rewriting
- does not require WPF to execute
- keeps Windows-specific concerns at the shell edge

Examples of good future-facing seams:
- library query service
- capture detail service
- comment update service
- star toggle service
- cover-art read service
- game summary projection service

## What To Watch For During Refactors

During refactors, do not chase cross-platform purity for its own sake.
The goal is not to make the desktop app portable.
The goal is to prevent important logic from being trapped in desktop-only code when it could reasonably live behind a service boundary.

A Windows-only implementation is still fine when the responsibility is clearly desktop-only.
Examples include:
- file picker workflows
- modal dialogs
- desktop-only maintenance utilities
- native shell integration

## Review Checklist For New Work

Before merging a feature or refactor, ask:
- Does this logic need to live in `MainWindow`?
- Could this become a backend responsibility later?
- Are the inputs and outputs plain models?
- Is this introducing unnecessary Windows or WPF coupling?
- If iOS eventually needs this behavior, will this structure help or hurt?
- Is this a desktop-only concern or a product-level concern?

## Practical Rule Of Thumb

If a feature is about:
- presentation, window behavior, dialogs, or desktop file handling, desktop-specific code is acceptable
- library data, capture data, stars, comments, queries, summaries, or lightweight user actions, prefer a service-oriented shape

## Execution plan (desktop refactor)

Concrete **staged slices** for thinning the Windows UI shell while respecting the guardrails above live in:

- **`docs/plans/PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md`** (`PV-PLN-UI-001`)

That plan ties **Phase 3** MainWindow shrink work to **service-shaped** and **DTO-friendly** boundaries where they overlap with future mobile/back-end consumers.

## Document Intent

This document is a development guardrail, not a strict implementation roadmap.
It should stay short, opinionated, and useful during normal coding work.
Update it when:
- the intended iOS scope changes
- the backend/service plan changes
- mobile-safe write actions expand or contract
- a new shared/core layer is introduced and the guidance should be tightened
