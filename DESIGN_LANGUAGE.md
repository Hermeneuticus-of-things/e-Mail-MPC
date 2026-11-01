# Hallmark design language

This is the canonical Hallmark design language for the owner's applications, including eMail MPC and applications developed in the future. It is a shared rule set, not an eMail MPC-only visual theme or an optional layer beside another design system.

Each application must declare its own core function and foreground zone in its README or `DESIGN.md`, in one short sentence. Do not copy another application's declaration without verifying that it describes that application's actual work.

## Governing rules

1. **Core work is foreground.** The application's declared core work receives attention first; surrounding controls do not compete with it.
2. **The core function and foreground zone are explicit.** Each application declares them in its own README or `DESIGN.md`.
3. **Supporting controls stay available but in the background.** Menus, buttons, toolbars, and panels remain reachable, but stay small, discrete, and quiet.
4. **Use minimal size.** Controls are only as large as needed to read and use. Avoid wasted space and interface elements that take space away from the core work.
5. **No large buttons or oversized icons.**
6. **Use compact dropdowns for enumerated field values.** Use buttons for primary actions and confirmation, not as button bars for changing values.
7. **Keep the interface quiet.** Attention stays on the application's core work.
8. **Use a vector-scalable interface.** Windows are resizable; layouts reflow, and graphics stay crisp instead of stretching raster bitmaps.
9. **Hallmark is this document.** Do not treat it as a separate or optional design track.

## Available without taking over

Keep controls reachable without letting them dominate the work surface:

| Pattern | Use |
| --- | --- |
| Minimal persistent toolbar | Actions needed often |
| Collapsible panel | Secondary tools that would otherwise crowd email; collapsed by default when appropriate |
| Popover or overlay | Short contextual choices |
| Keyboard shortcut | Frequent actions |
| Distraction-free view | Only with a clear, documented way to restore controls |

Do not remove a control without leaving a clear path to it.

## Dropdowns and actions

Use compact dropdowns for fixed sets of values such as account, sort order, mode, or security protocol. Keep item heights compact, content widths bounded, selection clear, and keyboard navigation available. Long lists should support type-ahead or search when needed.

Use inline text fields for free text and suitable compact pickers for dates or numbers. Use compact toggles for on/off values. Do not replace an enum dropdown with a prominent row of buttons.

Reserve visible buttons for primary actions and confirmation. Keep secondary actions in menus or other compact, accessible controls. Make destructive actions visibly distinct without making them prominent. Hover, focus, active, disabled, loading, and error states should remain discreet and understandable.

## Menus and feature reachability

Compactness is not a reason to remove functionality or flatten a useful menu. Keep the application's implemented commands reachable. When there are many commands, organize the full set into clear menu categories and well-named submenus, with separators, keyboard shortcuts, or a command palette where they help users find work. Do not delete, hide permanently, or omit menu commands merely to make the interface look simpler. A compact dropdown is appropriate for changing an enumerated field value; it is not a substitute for preserving the application's broader command structure.

## Scale, accessibility, and layout

Use the following proposed defaults until product-specific tokens are adopted:

| Token | Proposed default |
| --- | --- |
| Control height | 24, 28, or 32 px |
| Icon size | 16 or 20 px |
| Interface text | 12–14 px |
| Spacing | 4, 8, or 12 px |
| Pointer target | At least 32 px |
| Touch target | At least 44 px |
| Motion | 120–200 ms, orientation only, no bounce |

Quiet must not mean low contrast. Provide WCAG AA contrast for text and essential controls, visible keyboard focus, full keyboard navigation, and screen-reader labels. Keep pointer and touch targets usable even if their visible glyphs are smaller. Respect reduced-motion preferences wherever motion is introduced.

Prefer compact density. On narrow surfaces, let supporting controls collapse or move to an overlay so email keeps the largest share of the space. Supporting panels must not permanently cover the message surface. Loading, empty, error, success, offline, and permission-denied feedback should be clear and discreet, without oversized dialogs or repeated disruptive notifications.

## Vector-scalable interface

Vectorial and resizable describe one requirement: the interface scales cleanly as its window or surface is resized.

- Keep application windows resizable and define practical minimum sizes.
- Use layout containers that reflow with available space rather than fixed, rasterized screen compositions.
- Use native vector controls, vector paths, or scalable assets for interface graphics; do not use pixel-baked artwork that blurs when enlarged.
- Test the interface at both minimum and larger window sizes. Ensure the message body remains usable and supporting controls stay reachable.

## Shared tokens and components

Where a shared Hallmark design system is available, use its size, type, spacing, and color tokens and its reference controls. Until then, each product should use consistent local tokens and follow these rules rather than inventing oversized one-off controls.

## Anti-patterns

Do not use oversized buttons or icons, prominent button bars for enum values, blurry raster graphics, inaccessible hidden controls, flashy motion, or low-contrast styling. Refer to supporting UI as **surrounding controls** or **interface elements**.

## Governance

- The owner ratifies exceptions and proposed token defaults.
- Update this document when the design language changes; products should follow the current version.
- Record any approved deviation in product documentation.
- Apply these rules as Hallmark for all product UI work; do not create a parallel optional design track.
- Before merging UI work, review it against the checklist below.

## Compliance checklist

- [ ] The product declares its core function and foreground zone.
- [ ] The core function is visually dominant.
- [ ] Buttons and icons are compact.
- [ ] Enumerated fields use compact dropdowns, or an exception is documented.
- [ ] Supporting controls stay reachable without competing with email.
- [ ] Windows are resizable and the interface remains crisp and usable at different sizes.
- [ ] Contrast, visible focus, keyboard navigation, and accessible names are provided.
- [ ] Frequent actions have keyboard paths.
- [ ] Documentation uses "surrounding controls" or "interface elements" for the UI.
- [ ] UI changes follow Hallmark as defined here, not a parallel optional layer.

## Project documentation

- [README](README.md)
- [Roadmap](ROADMAP.md)
