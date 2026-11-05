---
name: "Ionuț Application Design Language"
description: "Applies Ionuț's complete UX and UI rules to eMail MPC: focus levels, input and command routing, safe movement between surfaces, complete menus, compact controls, accessibility, vector scaling, and the Hallmark visual floor. Use before implementing, changing, or reviewing any application feature, interaction, or interface in this repository."
---

# Ionuț Application Design Language

## Mandatory project skill

This repository has adopted Ionuț Application Design Language. Apply this skill to every eMail MPC application task, not only visual redesigns. It governs user experience and interface behavior: active work surfaces, input and command routing, movement between windows and dialogs, focus restoration, feedback, and visual design. Do not treat it as optional inspiration.

The complete, user-supplied v1.0 specification is [`IONUT_APPLICATION_DESIGN_LANGUAGE.md`](IONUT_APPLICATION_DESIGN_LANGUAGE.md). Read it before work that changes application behavior, UI, focus, navigation, commands, or controls. That file is authoritative and stands alone; this skill is the project entry point and execution checklist.

## eMail MPC application context

- The core function is showing, reading, finding, organizing, and composing email. Keep mail content in the foreground.
- Preserve every existing command, menu item, setting, and workflow unless Ionuț explicitly asks to remove it. Rearrange and group; do not simplify by deletion.
- Compose opens with the message body focused. Opening a dialog or secondary surface moves input into that surface; closing it returns the user to the prior control and position.
- Keep account, folder, search, sort, read, compose, draft, attachment, backup/restore, and update functions discoverable.
- Follow the repository's Hallmark product foundation and UI tokens in the canonical design language. Do not invent product screens, unsupported features, or visual evidence.
- The standalone client must remain useful without an AI agent or AI service. MCP and embedded AI are optional future work unless implemented and verified.

## Before changing application behavior

1. Identify the user task and the surface/window that owns it.
2. Read the relevant canonical language sections and inspect the existing interaction and command paths.
3. Keep the active surface, focus/selection distinction, close/return behavior, keyboard access, and feedback consistent with the document.
4. Preserve data and user intent on errors, cancellation, retries, and recovery. Do not silently drop actions or show success when an operation is uncertain.
5. Add or update tests for behavior, including focus and navigation tests for new or changed surfaces where applicable.

## Before changing UI

1. Inventory the existing commands, menu items, settings, and reachable actions; verify the inventory does not shrink.
2. Implement UX and visuals together. Follow the canonical focus stack/routing requirements, not only visual focus rings.
3. Apply the specified size/spacing tokens, dropdown and button rules, visible focus, keyboard and screen-reader access, contrast, and vector scaling.
4. Keep all actions available through a discoverable menu, toolbar, or equivalent path. Do not remove complex menus or nested options for aesthetics.
5. Run the conformance checklist in §17 of the canonical document. Report any unmet item plainly; never claim full conformance if it was not verified.

## Completion handoff

State what changed, how the user-visible workflow behaves, what tests/checks ran, and any remaining limitations. Do not claim a roadmap feature is complete merely because it is documented.
