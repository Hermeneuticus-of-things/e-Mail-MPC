# Ionuț Application Design Language

**Version:** v1.0 · **Date:** 2026-11-01 (Europe/Paris)

Every change bumps the version and the date (§16). The number in this header is the only current version.

*Also recognized as:* **design application language** (the app-facing side: what the user sees, does, and moves through) and **design logic language** (the tool/engine side: how the app tracks the active surface, routes input, and dispatches commands). These are alternate names for this same document. File names stay unchanged: `IONUT_APPLICATION_DESIGN_LANGUAGE.md` (full) and `IONUT_APPLICATION_DESIGN_LANGUAGE.SKILL.md` (loadable skill).

This is a **general application design language**. It applies to **every application** — writing tools, news readers, email clients, AI assistants, image editors, dashboards, and any other app type. The rules do not change with product name or domain; only *which* surface is the core function changes.

**Purpose:** eliminate generic, noisy, AI-generated interface slop. Put the app's real work in front, give whatever the user is working in right now its own focus, and keep surrounding controls quiet, complete, and reachable.

**How the document builds (one arc, each part resting on the one before):**
§0 sets the scope (UX and UI) and the shared words → §1 decides *whether* this language is used on a project → §2 decides *where the user's attention and input are right now* (the core of the language) → §3 decides *what the product puts in front overall* → §4–§14 are the practical rules that every surface obeys while it is or is not the active surface → §15 is Hallmark (the skill + our product foundation under the Floor) that all of the above is drawn on → §16 keeps the document alive → §17 verifies and closes.

---

## Contents

- 0. Scope — UX as well as UI (0.1 Glossary · 0.2 How to read · 0.3 Acceptance bar)
- 1. Adoption — ask before every new project (any AI)
- 2. Focus levels (core) — includes 2.13 new cases that do not fit
- 3. Core function and completeness
- 4. Measurable scales
- 5. Disappear vs stay available
- 6. Dropdown specifications
- 7. Buttons and actions
- 8. Accessibility
- 9. Vector-scalable interface (one rule)
- 10. Surrounding controls (wording and weight)
- 11. Density and layout
- 12. Feedback and errors
- 13. Theming and motion
- 14. AI-assisted UI work
- 15. Hallmark — skill, Floor, themes, product foundation, Liquid Glass hybrid
- 16. Maintenance — living document
- 17. Conformance checklist and closing

---

## 0. Scope — UX as well as UI

**If you are implementing this, your job is:** apply these rules to how the app **behaves** (interaction logic, focus, movement through the app, engine state) **and** how it **looks** (layout, sizes, visuals) — never one without the other.

This design language covers **UX as well as UI**. It governs the whole user experience:

- **Interaction logic (UX):** whether the language is used at all (§1); which surface is active and receives input and commands (§2); what the product puts in front (§3); how controls hide and come back (§5); feedback (§12).
- **Movement through the app (UX):** opening and closing settings, menus, dialogs, panels, and images, and returning exactly to where the user was (§2.5).
- **Engine logic:** focus state, surface registration, input routing, command dispatch (§2.6–§2.8).
- **Visual interface (UI):** scales, dropdowns, buttons, density, theming (§4–§13), drawn on the Hallmark visual floor (§15).

**What this is NOT:** not a visual style guide alone; not a logo or brand book (the only visual identity it sets is the Hallmark visual floor in §15, which is a default floor, not a brand); not specific to one product.

### 0.1 Glossary (one meaning each, used the same way everywhere)

| Term | Meaning in this document |
|---|---|
| **Core function** | The app's primary job, which owns the product-wide foreground (§3). Writing app → the text editor; image editor → the canvas. One per app. |
| **Surface** | Any region the user can work *in*: text editor, settings page/panel, menu, dropdown, image viewer, inspector, side panel, list, dialog, command palette. |
| **Active surface** | The one surface that owns keyboard input, command dispatch, and visual emphasis *right now* (§2). Exactly one per top-level window. |
| **Focus level** | The nesting depth of a surface or object: **app → window → surface → object** (§2.2). |
| **Focus** (unqualified) | Always means §2 level-based focus: the active surface at its level. The control-level keyboard indicator is called the **focus ring** (§8). |
| **Selection** | What the user has chosen *inside* a surface (text range, list item, image layer). Selection is **not** focus (§2.4). |
| **Trigger** | The control or object from which a surface was opened (a menu item, a gear button, a shortcut acting on the caret position). |
| **Surrounding controls** | Menus, toolbars, panels, status bars, and other interface elements around the core function (§10). Do not call them "chrome", "stuff", or "bits". |
| **Hallmark visual floor** | The visual foundation every surface is drawn on: warm, opaque, line-art, content-first (§15). Not a separate product language. |

### 0.2 How to read this document

Every numbered section uses the same parts, in this order: the rule · **If you are implementing this, your job is:** (one line, the required action) · **Effort matters / Out of scope** · **Correct / Avoid** · **What this is NOT** (where the rule is easy to confuse). Rules are mandatory (§14, §17). Where a number is given it is the default; deviate only for a documented platform requirement, and write the reason in the code or the app's docs.

### 0.3 Acceptance bar

An implementer — human or any AI — holding **only this document plus the codebase** must be able to implement every rule correctly, without guessing and without opening any other design document or external guideline. Names of outside guidelines (Apple HIG, Material, Primer, Atlassian, WAI-ARIA, Carbon, Fluent, Polaris, Liquid Glass) appear only as credits for ideas already written out here in full; you never need to read them. If a rule cannot be implemented as written, that is a defect in this document (fix it per §16), not permission to improvise.

---

## 1. Adoption — ask before every new project (any AI)

**If you are implementing this, your job is:** at the start of every new project, before designing or writing any UI, ask the caller one yes/no question — whether to use this design language — and then apply it fully (yes) or not at all (no).

**Who this binds:** any AI, on any platform or product — OpenCode, Claude, Cursor, GitHub Copilot, Grok Bot, or any other assistant, agent, or tool — and any human developer handed this document. The rule is tool-agnostic: it depends on no feature of a specific product.

**Packaging:** this language ships as a skill file (`IONUT_APPLICATION_DESIGN_LANGUAGE.SKILL.md`, plain Markdown with a short YAML header) so any AI can load it — as a skill, a rules file, a system/project instruction, or simply by reading it. Loading the skill is not the same as adopting it for a project; adoption is the caller's answer to the question below.

**When to ask:** once, at the start of every **new project** (a new repo, a new app, or a new product area the caller names as a new project), **before** proposing layouts, generating UI code, or choosing components. Do not ask again for every task inside a project that already has an answer.

**How to ask:** as a yes/no choice (a yes/no widget if the tool has one; otherwise a clear one-line question). Exact wording:

> **Use the Ionuț Application Design Language for this project? (yes / no)**
> It covers UX and UI: focus levels, core function in front, complete menus, compact controls, the Hallmark visual floor.

**What to do with the answer:**

- **Yes** → load this document (or the skill), follow every section, and run the §17 checklist before handing over UI work. Record the answer in the project (e.g. one line in the README, `AGENTS.md`, or project instructions: `Design language: Ionuț ADL — yes`).
- **No** → do not apply these rules to that project; do not mention them again unless the caller asks. Record `Design language: Ionuț ADL — no`.
- **No answer yet** → do not design or write UI; wait. You may do non-UI work meanwhile.
- **Never assume silently** — not from a previous project, not from memory, not because the skill is installed.

- **Effort matters:** asking before the first UI decision; recording the answer where the next AI will see it.
- **Out of scope:** long explanations or sales pitches for the language; re-asking inside the same project.
- **Correct:** a new repo is opened → first message asks the yes/no question → caller says yes → the answer is written to `AGENTS.md` → UI work starts.
- **Avoid:** applying the language because "it was used last time"; asking after the UI is already built; asking for every pull request.

**What this is NOT:** not a licence check; not a per-feature question; not optional for the AI (asking is mandatory; using the language is the caller's choice).

---

## 2. Focus levels (core)

**If you are implementing this, your job is:** make the app always know which surface is active — text, settings, menu, image, panel, dialog, any surface — send keyboard input, commands, and visual emphasis to that surface at its own level, move focus into a surface when it opens, and on leaving it return focus to exactly where the user was.

### 2.1 The rule (caller's words)

Focus is **not only** on the app's overall purpose or task. Focus is also on the **active window** and on the **level at which the object sits**.

If you are writing text, focus is on that text. If you go from that text into settings, focus moves to the settings, so you can configure them properly. If you go into settings for something else, focus moves to that. If the menu is for something else, or a window or image is for something else, focus goes to that respective window.

So: if the app is only about writing, focus is on writing; but when there are intermediate things — settings, menus, images, panels, dialogs — that also need detail and attention, focus goes to whichever intermediate level is active. **Focus tracks both the active window and the level of the object, not just the app's scope.** Each task, each window, gets its own focus at its level.

| The user is working in… | Focus is on… |
|---|---|
| Text in the editor | That text, at the caret / selection |
| Settings opened from that text | The settings surface — configured with the same clarity and detail as the text |
| Settings for something else (another document, a tool, a plugin) | That other settings surface |
| A menu or submenu | That menu, and the highlighted item in it |
| An image (viewer, crop, inspector) | That image and its local tools |
| A panel, inspector, sidebar, or list | That panel and its current item |
| A dialog or confirmation | That dialog and its default control |

**The active window owns the work.** The window (and, inside it, the surface) the user is working in is the one that receives input and shows emphasis; focus follows it, and the app never pulls focus elsewhere on its own (credit: Apple HIG). The only moves of focus the app makes itself are the ones in §2.5 (opening and closing surfaces) and blocking feedback in §12.

### 2.2 The level model

Treat every app as nested levels, outermost to innermost:

1. **App level** — application menu bar, main navigation, global command palette.
2. **Window level** — a top-level OS window or a document/workspace tab.
3. **Surface level** — editor, image viewer, settings page or panel, side panel, menu, dropdown, dialog.
4. **Object level** — the paragraph or selection, the setting field, the menu item, the image layer, the list row.

Rules:

- Each **top-level window has its own focus stack** (§2.6). When the OS activates a window, that window's stack top becomes the active surface again, exactly as it was left.
- Inside a window, **exactly one** surface is the active surface. Its level decides what receives input, what commands mean, and what is emphasized.
- Outer levels stay **visible and reachable** (§3.1, §5); they are only secondary while an inner level is active.

### 2.3 What "receiving focus" means — all three, never only one

When a surface becomes active, the app does **all** of these:

1. **Input routing** — keys, wheel, and shortcuts go to the active surface first. `Ctrl+F` in settings searches settings; in the editor it searches the text.
2. **Command dispatch** — commands (menu items, palette entries, shortcuts, toolbar buttons) resolve against the active surface first, then bubble outward level by level only if unhandled (§2.7 lists which commands bubble).
3. **Visual emphasis** — the active surface is the most prominent: full-contrast header/title, active selection color, the emphasis tokens in §2.9. Inactive surfaces are de-emphasized only through their header and selection color — **never hidden, blurred, dimmed below AA contrast, or stripped of controls**.

### 2.4 Focus is not selection

Focus (which surface owns input) and selection (what is chosen inside a surface) are separate states and are stored separately (credit: Atlassian).

- **Do:** open Settings from the editor while a paragraph is selected → the paragraph stays selected, shown in the *inactive* selection color; the settings surface owns input. Close Settings → same paragraph, same selection, now in the active selection color.
- **Do:** in a Binder/outline list, the current document row stays marked while the user types in the editor — the row is selected, the editor has focus.
- **Don't:** clear the selection when focus leaves a surface; treat "selected" as "focused" (e.g. route `Delete` to the selected list row while the user is typing in the editor); show both with the same color so the user cannot tell which surface will receive keys.

### 2.5 Entering and leaving surfaces (open → in, close → back)

**Opening a surface moves focus into it** (credit: Primer / Material):

- The new surface is pushed on the focus stack and becomes active immediately — no extra click needed.
- **Initial target inside it**, first match wins: (1) the control that was last focused in this surface during this session; (2) its search/filter field, if it has one; (3) its first editable control in the first group; (4) the surface container itself. **Never** the Close button and **never** a destructive control.
- **Modal** surfaces (dialogs, confirmations): `Tab` cycles only inside them, and everything behind them is inert until they close. **Non-modal** surfaces (side panels, settings panel, inspectors, image viewer in a pane): `Tab`/`F6` and pointer clicks can move to sibling surfaces.

**Leaving a surface returns focus** (close, `Escape`, Back, completing it, or clicking away from a transient surface such as a menu or dropdown):

1. Restore focus to the **previous surface** — the one the user came from (normally the trigger's surface), not a default or "home" surface.
2. Restore the **position inside it**: same control or trigger, same caret and selection in text, same scroll, same list row or image layer.
3. If that surface no longer exists, go to its **nearest still-open parent** level and focus its last-focused object.
4. **Focus persistence** (credit: WAI-ARIA): focus is never left on nothing — not on a closed or hidden surface, not on the bare window/body. After **deleting** the focused item in a list, focus moves to the next item, else the previous item, else the list itself.
5. Never jump to app-level surrounding controls (menu bar, main navigation, title bar) **unless** the user closed the whole level that held the work (e.g. the document window).
6. **Siblings** (editor ↔ side panel ↔ inspector) switch directly; each remembers its own last object, caret, and scroll.
7. `Escape` closes the **innermost transient first** (open dropdown → open menu → popover → dialog → non-modal surface), one level per press. `Escape` in the core function surface does nothing at window level. While text input is composing (IME), `Escape` belongs to the input method.

Example: typing at word 120 → open Settings → change font size → `Escape` → caret back at word 120, same selection, typing continues. Wrong: caret at document start; focus on the toolbar; nothing focused.

### 2.6 Engine logic — the focus state (how to implement)

This is a logic rule, not only a visual one. Implement an explicit focus model in app state, independent of any UI framework. If the framework already has a focus manager, **wrap** it with this model; do not build a second competing one. If the codebase has none, create one module (e.g. `focus/`) that every surface uses.

```
FocusState {                       // one per top-level window
  stack: FocusEntry[]              // bottom = window level, top = active surface
  registry: Map<surfaceId, SurfaceRegistration>
}
FocusEntry {
  surfaceId: string
  level: "app" | "window" | "surface" | "object"
  triggerId?: string               // control/object that opened it (return target)
  lastFocusedControlId?: string
  caret?: { start, end }           // text surfaces
  scroll?: { x, y }
  selection?: any                  // list row, image layer, text range
}
SurfaceRegistration {
  id, level, parentId, modal: boolean,
  commands: Map<commandId, handler>,
  initialFocus?: () => controlId,  // optional override of §2.5 order
  onFocus(entry), onBlur(entry)    // emphasize / save caret, scroll, selection
}
```

**Surface ids** — pattern `<kind>.<scope>[.<instanceId>]`, lowercase, dot-separated:
`editor.document.<docId>`, `settings.app`, `settings.document.<docId>`, `settings.plugin.<pluginId>`, `menu.<menuId>`, `dropdown.<fieldId>`, `dialog.<dialogId>`, `panel.<panelId>`, `image.viewer.<assetId>`, `palette.commands`. Window level: `window.<windowId>`.

**Registration** — every surface that can take input registers at its level when created and unregisters when destroyed:

```
const unregister = registerFocusableSurface({
  id: "settings.document.42",
  level: "surface",
  parentId: "window.main",          // the window that contains it
  modal: false,
  commands: { find: searchSettings, close: closeSettings, reset: resetField },
  onFocus: (e) => { emphasize(); focusControl(e.lastFocusedControlId ?? defaultTarget()); },
  onBlur:  (e) => { e.lastFocusedControlId = currentControl(); e.scroll = scrollPos(); },
});
```

**Operations**

- `pushFocus(surfaceId, triggerId)` — on open: `onBlur` the current top (saves its state), push the new entry with `triggerId`, `onFocus` it (§2.5 initial target).
- `popFocus(surfaceId)` — on close: remove it **and any entries above it** (children close with their parent), `onFocus` the new top and restore its saved control/trigger, caret, scroll, selection.
- `switchFocus(surfaceId)` — sibling switch: replace the top without discarding the other sibling's saved entry.
- `dispatch(commandId, args)` — walk the stack top → bottom; the first surface whose `commands` has the command runs it; if none does, the command is shown disabled in menus/palette and does nothing.
- `onSurfaceDestroyed(id)` — unregister; if in the stack, pop it and apply §2.5 step 3.

**Implementation rules**

- **Single source of truth:** `stack.top` is the active surface. Components read it; none guesses focus on its own.
- **Native focus follows the model:** DOM/widget/OS focus is synchronized *to* `stack.top` — it is the effect, not the source. (Web: `el.focus({preventScroll:true})`, `inert` on content behind modals, `focusin` to detect pointer entry. Qt: `setFocus`, `QApplication::focusChanged`. WPF: `FocusManager`/`Keyboard.Focus`. Electron: window `focus`/`blur` events at window level.)
- **Pointer = keyboard:** clicking into a registered surface calls `switchFocus`/`pushFocus`, so mouse and keyboard users share one model.
- **Toolbar buttons do not steal focus:** clicking a toolbar/ribbon button (Bold, Insert, Zoom) dispatches its command to the active surface and focus **stays** where it was. A toolbar becomes the active surface only when the user moves into it by keyboard (`F6`/`Tab`), and `Escape` returns from it.
- **Unregistered input-taking surfaces are bugs.**
- **Tests (minimum):** open → close restores caret; nested open (settings → dropdown → dialog) → three `Escape`s return in reverse order; closing a parent pops its children; commands dispatch to the active surface and bubble when unhandled; deleting the focused list row lands on the next row; a toolbar click keeps editor focus; two windows keep separate stacks.

### 2.7 Which commands stay local and which bubble

| Command family | Handled by | Example |
|---|---|---|
| Find, select all, copy/cut/paste, delete, arrow/Tab navigation, local undo/redo inside a field, `Escape`/close | **Active surface** (local) | `Ctrl+F` in settings filters settings |
| Surface-specific actions | **Active surface** | Reset field (settings), Rotate (image), Merge cells (table) |
| Save, Save as, Print, Export, Close document | **Window level** (bubbles up) | `Ctrl+S` in settings saves the document |
| New, Open, Quit, Preferences, toggle panels, command palette, Help | **App level** (bubbles up) | `Ctrl+P` palette from any surface |

A surface may override a bubbling command only when it has a genuinely local meaning (e.g. a text field in an image dialog handles `Ctrl+A` as select-all-text). The command palette and menus list the **active surface's** commands first, then window, then app.

### 2.8 Reference case — a Settings surface in a writing app

This is the worked example every other surface follows.

- **Id and level:** `settings.document.<docId>` (document settings) or `settings.app` (app preferences), level `surface`, `parentId` = the containing window.
- **Form:** a non-modal side panel or page inside the same window when the user benefits from seeing the effect on the text live (font, spacing, page setup); a modal dialog only for one-shot choices that must be confirmed. App preferences may open as their own window, which then has its own stack.
- **Open:** from the Format/Tools menu, a gear button, or a shortcut → `pushFocus("settings.document.42", triggerId)` → initial target per §2.5 (search field if present, else first field).
- **Inside:** groups with section labels (§11), fields per §6 (dropdowns for 3+ choices, toggles for on/off, inline edit for numbers/text), one primary button at most (§7). Changes apply live where cheap and reversible, with a per-field reset; use explicit Apply/Cancel only when a change is expensive or irreversible.
- **Commands:** `find`, `reset`, `close` local; `save`, `export` bubble to the window; `palette`, `quit` bubble to the app.
- **Close:** `Escape`, close button, or Back → `popFocus` → editor caret, selection, and scroll restored.
- **Emphasis:** settings header uses the active tokens (§2.9); the editor keeps showing the text at full readability, with its selection in the inactive color.

### 2.9 Visual emphasis tokens (active vs inactive)

Define these as named tokens (values come from the theme and the Hallmark floor, §13, §15):

| Token | Active surface | Inactive surface |
|---|---|---|
| `focus.surface.header.text` | `text.primary` | `text.secondary` |
| `focus.surface.border` | 1 px `border.active` (or a 2 px top accent line) | 1 px `border.subtle` |
| `selection.bg` | `selection.active.bg` (app or OS accent) | `selection.inactive.bg` (neutral, still visible) |
| `focus.ring` | 2 px ring on the focused control (§8) | none |
| Content text | unchanged, full contrast | unchanged, full contrast (never dimmed below AA) |

No blur, no opacity over content, no scaling or animation is required to show which surface is active.

### 2.10 Aligned reinforcements

Dense panels and data views keep full keyboard rigor — every field and row reachable and operable without a pointer (credit: Carbon). The same focus behavior holds across platforms and input types, so a user moving between Windows, macOS, Linux, and web meets one model (credit: Fluent). These are patterns — behaviors across surfaces — not only components, which is why they live in engine logic and not in a widget library (credit: Polaris).

### 2.11 Effort matters / out of scope

- **Effort matters:** active-surface tracking per window; routing and dispatch; exact return (surface + control + caret + scroll); focus vs selection; registration of every surface; the §2.6 tests.
- **Out of scope:** the animation of focus transitions; the exact hue of the active header (comes from §13/§15); a particular framework or library — any implementation meeting §2.6 is acceptable; persisting the focus stack across app restarts (session-only is enough; per-document caret persistence is optional).

### 2.12 What this is NOT

- **Not focus rings alone.** The ring (§8) is required, but §2 is about which surface and level owns input, commands, and emphasis. A ring on the right button with keys going to the wrong surface fails §2.
- **Not one global focus forever.** Focus is not locked on the core function; intermediate surfaces get real focus while active.
- **Not stripping menus or controls.** De-emphasis never removes or hides controls (§3.1, §5).
- **Not the same as §3.** §3 is the product-wide foreground (what the app is for). §2 is which surface is active this moment. Both apply together.
- **Not platform keyboard-only focus guidance.** Tab order and rings still apply (§8); this adds level-based tracking, routing, dispatch, and return.
- **Not distraction-free / "focus mode".** Hiding all surrounding controls is a separate optional feature, not what this rule requires.

**Correct:** Settings opened from text get emphasis and input; `Escape` returns the caret to where it was; the toolbar Bold button bolds the selection without moving focus.
**Avoid:** Settings open but typing still goes into the document; closing a dialog leaves nothing focused; closing settings sends focus to the menu bar; hiding the toolbar "to focus the user".

---

### 2.13 New cases that do not fit an existing level

**If you are implementing this, your job is:** when a request or surface does not map cleanly to an existing focus level or section, stop and ask — do not invent a level or force the case into the wrong one.

**Direction:** The documented levels and surface kinds (text/editor, settings, menu, dropdown, image viewer, panel, list, dialog, palette, window, and the ids in §2.6) are the closed set until the caller extends them. A new kind of work (for example a live canvas that is neither image nor text, a collaborative presence strip, a timeline scrubber that owns its own input, a map, a 3D viewport, a terminal pane) is a **candidate**, not a free invention.

**Rule:**

1. **Do not guess.** If the case does not fit an existing level or surface kind, do **not** invent a new level, rename an existing one, or stretch a nearby kind to cover it.
2. **Ask or flag.** Ask the caller how it should sit in the level model, **or** flag it explicitly as a **new level / surface-kind candidate** with: what the user is doing there, what owns input, what commands stay local, and what close/return should restore.
3. **Document first.** Record the decision in this document (§2.6 ids, glossary if a new term, a short example) **before** implementing focus routing for that case.
4. **Then implement.** Only after the documented decision exists may the surface be registered and shipped.

**Correct:** "This live waveform scrubber is not text, settings, menu, or image — flagging as a new surface-kind candidate; waiting for the caller's decision before registering it."  
**Avoid:** silently treating it as an image viewer; inventing a fifth focus level above "object"; copying Settings' modal rules onto a free-floating timeline because "it felt close."

**What this is NOT:** not permission to stall forever on ordinary cases that clearly map (a normal dialog is still a dialog); not a bypass of §16 maintenance once the caller decides; not an excuse to leave focus unregistered after the decision is written.


---

## 3. Core function and completeness

**If you are implementing this, your job is:** name the app's single core function, give its surface the largest area and visual priority and make it the active surface when a window opens, and keep every existing surrounding control available around it.

Define the **core function** first. It occupies the product-wide foreground; everything else supports it.

| App type | Core function in the foreground |
|---|---|
| Writing app | Writing (the text editor) |
| News app | Showing news |
| Email app | Showing emails |
| AI app | The AI interaction |
| Image editor | The image being edited |
| Any other app | That app's primary job |

The language does not change with app type; only which surface is the core function changes.

**"Foreground" concretely:** the core function surface gets the largest area of the main window and the highest-contrast content, and it is the **active surface when a window opens** (§2). Surrounding controls (§10) sit at the edges at §4 sizes. When another surface becomes active (§2), the core function stays visible and readable; it simply is not the active surface until the user returns.

### 3.1 Completeness is required — do not strip controls

Putting the core function in front does **not** mean removing controls. Do not "simplify" by deleting menu items, submenus, or settings the user needs. Even when tasks, menus, submenus, and sub-submenus are complex, **keep them all** and arrange them clearly. Only the presentation is quiet; the functionality is complete.

"Needed" = any command, menu item, or setting that exists in the current app, unless the human owner explicitly asks to remove it. When unsure, keep it.

- **Effort matters:** identifying the core function correctly; preserving every command; organizing menus into clear groups.
- **Out of scope:** marketing copy, hero sections, onboarding illustrations, decorative imagery inside the app.
- **Correct:** a writing app with a large editor, a compact toolbar, and a full menu bar with all submenus intact; all AI commands gathered in one AI menu so the other menus stay clean (Scriptorium `aimenu.png`).
- **Avoid:** deleting the Export submenu because it "clutters"; a dashboard home screen pushing the editor below the fold.

**What this is NOT:** not minimalism by deletion; not "focus mode"; not §2 (product-wide foreground vs. the active surface right now).

---

## 4. Measurable scales

**If you are implementing this, your job is:** define these values as named tokens, use them for every control, and replace any off-scale size with the nearest token.

| Token | Values |
|---|---|
| Control height | **24 / 28 / 32 px** |
| Icons | **16 / 20 px** |
| UI typography | **12–14 px** |
| Spacing | **4 / 8 / 12 px** |
| Corner radius | **4–8 px** — pick one value per app |
| Control padding | **4–8 px** horizontal; height from the control-height token |
| Toolbar height | **32–40 px** |
| Minimum click target | **32 px** pointer · **44 px** touch |

- Values are at 100% UI scale and scale proportionally with zoom (§9).
- "UI typography" = labels, menus, toolbars, status text. **Content** typography (document text, email body, article) follows the content and user preference, not the 14 px cap.
- A click target may be larger than the visible control (a 24 px control with a 32 px hit area is correct).
- Tokens in code (e.g. `control.height.md = 28`), never repeated literals.

- **Effort matters:** consistency across the app; tokens in code; target sizes.
- **Out of scope:** pixel-matching native widgets that already meet the scale.
- **Correct:** 28 px buttons, 16 px icons, 8 px gaps in a toolbar.
- **Avoid:** 56 px buttons; 32 px toolbar icons; 18 px menu labels; random 10/15 px spacing.

**What this is NOT:** not a limit on content text size or on the core function's area.

---

## 5. Disappear vs stay available

**If you are implementing this, your job is:** make every hidden surrounding control come back from a fixed, discoverable place, and never remove a command's last access path.

Patterns, in order of preference: (1) minimal persistent toolbar; (2) collapsible panels — hide density, not capability, restore in one click or shortcut; (3) popover/overlay — temporary detail without leaving the core function; (4) command palette — the full command set by keyboard; (5) keyboard shortcuts for frequent actions, documented.

- **A control may hide** only when an equally discoverable path remains (menu, palette, or toolbar overflow). A shortcut alone is not enough.
- **It reappears** from the same entry point every time; collapsed state is remembered for the session.
- **Concrete test:** every command has, at all times, at least one of {menu item, palette entry, visible toolbar button}.
- **Focus link:** opening a popover or palette is a `pushFocus` (§2.5); closing it returns focus to where the user was.

- **Effort matters:** a palette with the full command set; one consistent restore control per collapsible panel.
- **Out of scope:** idle auto-hide timers; collapse animation.
- **Correct:** a side panel collapses to a 32 px strip with an expand button; its commands are also in the menu and palette.
- **Avoid:** a toolbar that vanishes on scroll with no way back; features reachable only through an undocumented gesture; hover-only reveals with no keyboard path.

**What this is NOT:** not "show everything at once".

---

## 6. Dropdown specifications

**If you are implementing this, your job is:** replace large button rows and icon banks used to choose a field value with compact dropdowns per this table, and leave the listed exceptions alone.

| Topic | Rule |
|---|---|
| Field types | Enumerations, modes, presets, sort/filter, status, view options |
| Dropdown vs toggle | Toggle for binary on/off; dropdown for 3+ choices |
| Two non-binary choices | Segmented control or dropdown |
| Dropdown vs inline edit | Inline edit for free text and numbers the user types |
| Max width | Fit content; cap ~280–320 px unless labels need more |
| Item height | Control height (24/28/32 px) |
| Long lists | Search/filter above ~12 items |
| Keyboard | Arrows, Enter selects, Escape closes, typeahead |
| Selected state | Check or highlight; never color alone |
| Grouping | Optional section labels |
| Focus | An open dropdown is the active surface `dropdown.<fieldId>` (§2); closing returns focus to its trigger field |
| Exceptions | Primary commit actions, live readouts, numeric steppers, free-text fields are **not** forced into dropdowns |

- **Effort matters:** keyboard behavior; clear selected state; search on long lists.
- **Out of scope:** custom dropdown animation; replacing native selects that already comply.
- **Correct:** "Sort by" dropdown with 5 options, 28 px items, a check on the current one.
- **Avoid:** five large buttons for sort order; a 40-item font list without search.

**What this is NOT:** not "turn every action into a dropdown"; not a rule for navigation menus (§10–§11).

---

## 7. Buttons and actions

**If you are implementing this, your job is:** keep visible buttons only for the primary action and confirmations, move secondary actions into menus, small icons, or the palette, and give every actionable control all six states.

- Buttons are for a **primary action** or an explicit **confirmation**; sizes per §4.
- Hierarchy: **primary / secondary / tertiary / destructive**; at most **one primary** per surface.
- States: **hover · focus · active · disabled · loading · error**.
- Destructive + irreversible → confirm; reversible (undo exists) → prefer undo over a confirmation dialog.
- The default control of a dialog (§2.5) is the safe one, never the destructive one.

- **Effort matters:** one clear primary per surface; all six states; destructive confirmation.
- **Out of scope:** gradients, shadows, decorative icons in buttons.
- **Correct:** a dialog with one primary "Save", a secondary "Cancel", and a distinct small "Delete".
- **Avoid:** three equal large buttons; a disabled state that looks enabled; deleting without confirmation and without undo.

**What this is NOT:** not a ban on buttons or on icon buttons in toolbars (allowed at §4 sizes).

---

## 8. Accessibility

**If you are implementing this, your job is:** make every control reachable and operable by keyboard and screen reader, with AA contrast and a visible focus ring, in every theme.

Quiet does **not** mean low contrast.

- **WCAG AA:** 4.5:1 normal text; 3:1 large text, essential icons, and control boundaries.
- **Focus ring** visible on keyboard navigation (2 px, `focus.ring` token).
- **Full keyboard** access to every action; logical tab order within each surface.
- **Screen reader** name, role, state on every control; announce when the active surface changes (dialog/panel title).
- **Reduced motion** honored; no essential information only in animation.

- **Effort matters:** contrast checks; tab order; labels on icon-only controls; ring visibility.
- **Out of scope:** exceeding AA (AAA welcome, not required).
- **Correct:** a 16 px icon button with an accessible name and a visible 2 px ring.
- **Avoid:** light-grey labels "to look quiet"; unlabeled icon buttons; ring removed in CSS.

**What this is NOT:** the focus ring is the **control-level** indicator; it does not replace §2 (surface-level focus, routing, return). Both are required.

---

## 9. Vector-scalable interface (one rule)

**If you are implementing this, your job is:** make the whole interface scale crisply at any resolution and zoom with vector assets and tokens multiplied by the UI scale factor, with no separate scaling modes.

**One rule:** layout, icons, and controls scale cleanly with resolution and user zoom as vectors (or equally crisp scaling), without separate "raster" and "vector" product modes. Tokens stay proportional when the scale factor changes.

- **Effort matters:** vector icons (SVG / icon font / platform vector); token × scale; testing at 100%, 150%, 200%.
- **Out of scope:** user content images (photos in a document are content).
- **Correct:** at 200%, a 28 px control is 56 physical px and its 16 px line icon stays sharp.
- **Avoid:** blurry PNG toolbar icons; a "High-DPI mode" toggle; layouts breaking at 150%.

**What this is NOT:** not converting user images to vectors; not a responsive-redesign rule.

---

## 10. Surrounding controls (wording and weight)

**If you are implementing this, your job is:** name supporting UI precisely in code, docs, and reviews, and keep it visually secondary to the core function while fully reachable and able to become the active surface.

Call supporting UI **surrounding controls**, **interface elements**, **menus**, **toolbars**, or **panels**. Do not use "chrome" or vague slang ("stuff", "bits").

Surrounding controls: stay visually in the background (small, discrete, readable); never take the foreground from the core function; remain reachable (§3.1, §5); become the **active surface** when the user works in them (§2) — except toolbar buttons clicked by pointer, which act on the active surface without taking focus (§2.6).

- **Effort matters:** precise names in components and docs; secondary visual weight.
- **Out of scope:** renaming existing code identifiers only for wording.
- **Correct:** "Formatting toolbar", "Layers panel", "File menu", "Binder panel", "Inspector panel".
- **Avoid:** "the UI stuff around the editor"; a toolbar louder than the document.

**What this is NOT:** "background" means visual weight, not hidden or inaccessible.

---

## 11. Density and layout

**If you are implementing this, your job is:** group related controls compactly on the §4 spacing scale, keep one clear hierarchy per view, and organize — never delete — nested menus.

- Compact grouping over scattered large hit areas.
- Spacing: **4** within a control group, **8** between controls, **12** between groups.
- One hierarchy per view: core function → local tools of the active surface → app-level navigation.
- Nested menus are expected in complex products — organize with separators and section labels; never delete for aesthetics.

- **Effort matters:** grouping by task; consistent spacing; menu organization.
- **Out of scope:** making every screen fit without scrolling.
- **Correct:** a menu with grouped sections and two-level submenus; toolbar groups 12 px apart.
- **Avoid:** controls scattered across the window; flattening a submenu into an endless list; removing a submenu "for cleanliness".

**What this is NOT:** not "cram everything" — density stays readable and within §4 targets.

---

## 12. Feedback and errors

**If you are implementing this, your job is:** show progress, errors, and success where they relate, quietly and actionably, without blocking the core function for short waits and without stealing focus.

- **Loading:** in place on the control or a small status; no full-screen takeover for waits under ~2 s; longer operations show progress and are cancellable when possible.
- **Errors:** actionable text next to the cause (what happened + what to do).
- **Success:** quiet status or brief toast.
- **Focus:** feedback never moves focus (§2) unless the user must act on it; a blocking error dialog is a `pushFocus`, and dismissing it returns focus per §2.5. Non-blocking messages are announced to screen readers without moving focus.

- **Effort matters:** placement next to the cause; actionable wording.
- **Out of scope:** custom illustrations for empty/error states.
- **Correct:** "Could not save — disk full. Free space or choose another folder." under Save.
- **Avoid:** "Something went wrong."; a full-screen spinner for 300 ms; confetti on save; a toast that grabs keyboard focus.

**What this is NOT:** not a ban on dialogs for errors that need a decision.

---

## 13. Theming and motion

**If you are implementing this, your job is:** keep AA contrast in every theme, take every color from tokens built on the Hallmark floor (§15), and keep motion optional, short, and never required to understand state.

- Light and dark if the product has themes; AA in both. The light theme is the Hallmark floor (§15); a dark theme keeps the same warmth (warm near-black surfaces, not cold blue-black).
- Colors from tokens, never hard-coded per component; §2.9 emphasis tokens included.
- Motion is optional polish (≤ ~200 ms for panels), skipped under reduced motion; no decorative animation delays the core function.

- **Effort matters:** contrast in all themes; reduced motion honored; tokens.
- **Out of scope:** adding themes the product does not have; elaborate transitions.
- **Correct:** a 150 ms panel slide skipped with reduced motion.
- **Avoid:** a 1 s intro animation before the editor is usable; dark theme with 3:1 body text.

**What this is NOT:** not a requirement to add a dark theme; the palette itself lives in §15, not here.

---

## 14. AI-assisted UI work

**If you are implementing this, your job is:** change UI toward this document without removing anything, implement focus as engine logic, and report what you changed with a command count and the §17 checklist.

When an AI edits UI (after §1 adoption = yes):

1. Preserve every command, menu item, and setting unless the human explicitly removes it.
2. Prefer rearranging and resizing over deleting.
3. Apply §4 scales and the §15 visual floor.
4. Do not invent hero buttons, empty marketing panels, or decorative filler.
5. Treat this document as mandatory, not inspiration.
6. Implement §2 as engine logic (§2.6), not only styling.
7. Run §17 and report failing or out-of-scope items.

- **Effort matters:** a before/after command inventory (count must not drop); checklist results.
- **Out of scope:** rewriting unrelated business logic; changing product features.
- **Correct:** "Moved 6 toolbar actions into the Format menu; all 42 commands still present; Settings registered as `settings.document.<id>`; focus restore tested."
- **Avoid:** "Simplified the UI" with 10 commands missing; a landing page nobody asked for.

**What this is NOT:** not a licence to redesign features; not optional.

---

## 15. Hallmark — the skill, the Floor, and our product foundation

**If you are implementing this, your job is:** treat Hallmark as the full anti-slop **design skill** (Floor, themes-as-materials, deal/draw, job anatomy, real material, sloplint gates, named redesign/audit), draw every app surface on **our chosen product foundation** under that Floor (warm opaque corkboard/paper, line-art — not Hallmark itself), and keep Liquid Glass as an **effect layer on chrome only** (§15.8).

**Source skill (installed):** [nutlope/hallmark](https://github.com/nutlope/hallmark) — globally at `~/.agents/skills/hallmark` (also copied for Claude Code / Cursor / OpenCode / Codex by `npx skills add nutlope/hallmark -g`). Tagline: *a design skill for Claude Code, Codex, Cursor and OpenCode that refuses to look AI-generated.* Powered by Together AI. MIT.

### 15.1 Two things that must stay separate

| Name | What it is | What it is NOT |
|------|------------|----------------|
| **Hallmark-the-skill** | The full Nutlope Hallmark skill: Floor, fifty themes as materials, deal-and-draw flow, job-based anatomy, real material only, Floor/Reflex/Finish gates via `sloplint`, redesign/audit only when named. | Not our corkboard palette. Not a single product look. |
| **Hallmark-as-our-product-foundation** | The warm, opaque, line-art look chosen for apps under this design language (Scriptorium `assets/readme/` corkboard/hero/aimenu and related). One **product theme under the Floor**. | Not Hallmark itself. Not permission to ignore the Floor. |

§2 decides *where* attention goes; Hallmark-the-skill decides *that the result must not look generated*; our product foundation decides *the material this product is drawn in*. Liquid Glass is only chrome effects (§15.8).

An older `docs/DESIGN_LANGUAGE.md` titled "Hallmark design language" may exist; its rules that still hold are carried here. **This document wins on conflict.** When UI work needs the live skill (new page, redesign, audit), load `~/.agents/skills/hallmark/SKILL.md` (or the Claude/Codex copy) — do not invent a parallel Hallmark.

### 15.2 Hallmark-the-skill — full scope (put it all in)

Hallmark holds a hard floor and then gets out of the way. It deals themes from a catalog of **fifty materials**, builds around what the visitor (or user) came to do, and lints against machine-made tells. A fresh-context reviewer gets the last word on page builds.

**What it covers beyond colour:**

1. **The Floor** — non-waivable anti-slop rules (tokens, motion, type tells, access, honesty, house bans). See §15.3.
2. **Fifty themes as materials, not page plans** — each theme is paper/type/colour/surfaces/components/texture with a picture; it never dictates what a page contains or in what order. Two prompts in one theme make two different pages. Custom drops themes and designs under Floor + gates only.
3. **Deal-and-draw build flow** — pre-flight → ask once (material, audience, use, tone) → shape from the job → deal three fitting themes (or custom) → load layout/anatomy/imagery/craft/genre/theme + gates → Picks block → build (hero first) → sweep with `sloplint --render` + judged gates + finish review.
4. **Job-based page anatomy** — name what the visitor comes to do (subscribe, book, buy, try, …); build from that category's real anatomy, never a default of figures / steps / email band.
5. **Real material only** — never invent metrics, testimonials, logo walls, quotes, or case-study counts. Screenshots of the real product when reachable; library photos credited; absent proofs stay absent.
6. **Gates in three tiers** — **Floor** (never waivable; FAIL blocks ship), **Reflex** (model defaults; fix or waive on the record), **Finish** (craft detail; answer, never blocking). Implemented by `scripts/sloplint.mjs` (static + `--render` / `--url`).
7. **Redesign / audit only when named** — Hallmark never starts on an existing design unless the user says `/hallmark redesign <path>` or `/hallmark audit <path>`. Redesign keeps content and wiring; audit only reports. Any other change to an existing design uses the project's own system.

**Harnesses:** Claude Code `~/.claude/skills/hallmark`; Codex/agents `~/.agents/skills/hallmark`; Cursor and OpenCode find those copies. Invoke by naming Hallmark or `/hallmark <prompt>`.

### 15.3 The Floor — non-waivable rules that apply to app UI

These bind every Hallmark output and, under this design language, every app surface drawn for a project that adopted Ionuț ADL. None are waivable. (Full gate list lives in the skill's `references/gates.md`; this is the Floor that always applies to **app UI**, stated so an implementer does not need another doc for the house bans.)

1. **Tokens only** — every colour and font goes through a declared token (`:root` or theme block). No literal colour or `font-family` past the token block.
2. **Motion discipline** — never `transition: all`; never animate `width`, `height`, `top`, or `left`. Prefer `transform` / `opacity`. Every animation has a `prefers-reduced-motion: reduce` fallback; content remains usable with scripting off (§8, §13).
3. **Type honesty** — never one italic word inside an otherwise roman heading; never an eyebrow above a heading; never tracked-capital / small-caps labels for UI chrome (labels stay sentence or lower case, untracked, body or label face).
4. **No purple-to-blue (or purple-to-pink / cyan-on-dark) gradient** as a signature look; no card-inside-card nesting as decoration; **no emoji as icons** in menus/toolbars/status; no hand-drawn fake browser/OS window frames.
5. **Focus and access** — every interactive control has `:focus-visible` (or platform equivalent) appearing instantly; AA contrast (§8); content visible without depending on colour alone.
6. **No invented facts** — never invent metrics, testimonials, logo walls, or case-study counts in UI copy or marketing surfaces.
7. **Overflow and layout hygiene** — no `100vw` horizontal blowout; image/grid tracks that can shrink use `minmax(0, 1fr)`-style constraints; display heads can wrap without clipping.
8. **Hero / first screen (for marketing or start pages)** — headline, one sentence, and primary action visible at ~1280×800 without scrolling when that pattern applies; do not pad with empty frames or photo-only wallpaper.
9. **Stamp when building pages with the skill** — first CSS comment lines carry the Hallmark stamp, critique, and any waivers; then run `sloplint` before handoff.

**Reflex** (fix or waive on the record with a real page-specific reason) and **Finish** (answer, never block) sit above the Floor; do not promote them to Floor. Past three identical Reflex waivers → house style belongs in a locked `design.md`.

### 15.4 Themes are materials — our corkboard is one product foundation

A Hallmark theme is a **material** (paper, type, colour, surfaces, texture, its own components). It is **not** a wireframe and not "the Hallmark look."

**Our product foundation** (chosen under the Floor for apps under this language):

- Warm paper ground `#F3EFE6`; cards/page `#FFFEF6`; text `#1F1B16` / `#3D362D`.
- Matte cork `#B98F5F` boards with index cards; muted-red stamped status labels when they carry real information.
- Copper/warm-brown accent for marks; selection may follow OS accent via §2.9 tokens.
- Single-weight line icons 16/20 px; **no emoji** in surrounding controls.
- Serif for content / README headlines; sans 12–14 for UI.
- Content-first screenshots of the real app — no fake frames, kickers, stock gradients, AI orbs.
- **Opaque matte content surfaces** — flat fills, 1 px borders, small radius, soft shadow at most. Liquid Glass only on chrome (§15.8).

**Source of truth for this product look:** Scriptorium `assets/readme/` (`hero.png`, `corkboard.png`, `aimenu.png`, related shots and SVGs) plus Hallmark Floor comments in code.

Other Hallmark catalog themes may be dealt for **new marketing/landing pages** when the skill runs; they do not silently replace this product foundation inside the app chrome and work surfaces unless the caller locks a different `design.md`.

### 15.5 Deal-and-draw, job anatomy, real material (when building with the skill)

When the user names Hallmark for a **new** page or UI:

1. **Pre-flight** the project (fonts, palette, framework, locked `design.md`, real product URL/app).
2. **Ask once** for material, audience, use, tone (or infer on "go ahead" and stamp inferences — never invent facts).
3. **Shape from the job** — what the user came to do; read that job's anatomy; design the page; name a signature.
4. **Direction** — named theme → use it; "custom" → Floor+gates only; else **deal** three themes and pick one material that can carry the prompt.
5. **Draw** (theme builds) deals shape / hero posture / nav / footer / first section / action placement — take it unless material refuses a pick; mirror-test against neighbouring prompts.
6. **Build hero first**, then the rest; stamp; tokens; log.
7. **Sweep** — `sloplint --render` until Floor clean; fix or waive Reflex; answer Finish; finish review in fresh context; close with the material ask.

**App screens and components** under Ionuț ADL still obey §2–§14 and §15.3–§15.4 even when a full Hallmark page flow is not run.

### 15.6 How the product foundation reinforces focus levels

- Active surface = brightest paper + full-contrast header (§2.9); inactive = neutral header/selection, content still readable.
- Surrounding controls stay quiet (line icons, small labels) so the eye lands on the work.
- Cork/card warmth belongs on **content** surfaces, never as the fill of toolbars/menus.
- One complete AI menu = complete, grouped, quiet until open — then it is the active surface (§2, §3.1).

### 15.7 Redesign and audit — only when named

- **Do not** run Hallmark redesign/audit on an existing UI unless the caller names it (`/hallmark redesign …`, `/hallmark audit …`, or clear spoken equivalent).
- Redesign keeps content and wiring; records before/after; fails if kept links/fields/handlers/headings go missing.
- Audit reports only.
- Polish / restyle / "keep the layout" are narrower modes when the caller says so.
- Unnamed edits to an existing design follow this document and the project's system — not a silent Hallmark rebuild.

### 15.8 Liquid Glass — effect layer (adopted hybrid)

**Roles are split — do not merge into one material.**

| Role | Owner | What it is |
|------|--------|------------|
| **Foundation (base)** | Our Hallmark product foundation under the Floor | Warm, **opaque** surfaces: corkboard/paper, line-art, content panels. Always the body of the app. Never replaced by glass. |
| **Effect layer (chrome / overlays only)** | Liquid Glass *effects* | Translucency, depth, specular highlights, adaptive surfaces — selectively on toolbars, floating panels, menus, transient chrome above the base. |

**Principle retained:** interface chrome remains **secondary to content**.

**Rules:** (1) Foundation owns every content/work surface. (2) Glass effects only on chrome/overlays — never filling the core function or text surfaces. (3) Effects must not steal focus (§2, §3). (4) If translucency drops AA over the base, drop the effect. (5) Do not blend into one muddy material.

**Correct:** warm opaque editor + lightly translucent floating toolbar.  
**Avoid:** glass-filling the window; see-through text; refractive icon blobs; merging warmth and glass into one look.  
**NOT:** full Apple Liquid Glass as the product identity; rejecting all chrome depth; putting glass on content.

### 15.9 Effort matters / out of scope / Correct · Avoid · NOT

- **Effort matters:** load Hallmark skill when named; obey the Floor on every adopted UI; keep product foundation tokens + line icons; run sloplint on Hallmark page builds; keep redesign/audit gated; keep Liquid Glass on chrome only.
- **Out of scope:** recreating cork texture pixel-for-pixel; custom brand illustrations unless asked; waiving Floor gates; inventing facts to fill empty UI.
- **Correct:** Floor-clean settings on warm paper; a dealt theme for a new landing page; corkboard start screen as product foundation; translucent menu over opaque editor.
- **Avoid:** treating corkboard colours as "all of Hallmark"; purple-blue gradients; emoji toolbar icons; silent redesign of an existing screen; glass body panels.
- **What this is NOT:** not a second product language beside Ionuț ADL; not permission to decorate; not a replacement for §4 sizes or §8 contrast; not Hallmark = one palette.


---

## 16. Maintenance — living document

**If you are implementing this, your job is:** when a new app is built or a rule changes, update this document incrementally in the same pattern and mirror the change everywhere it lives, in the same session.

- **When:** every new app (add its core function to §3, any new surface kinds to §2.6 ids) and every rule change the caller makes.
- **Version:** every change **bumps the version** in the document header (e.g. v1.0 → v1.1) **and** updates the **date** (Europe/Paris). The header version is the only current version; never leave two conflicting numbers. Skill mirrors must show the same version.
- **How:** add to the existing structure — new subsection, new table row, new example — instead of rewriting from zero. Keep each section's pattern: rule · "If you are implementing this, your job is:" · effort/out of scope · correct/avoid · what this is NOT. Keep the glossary (§0.1) as the single meaning of each term; add a term there before using it.
- **Recorded decisions** (like §15.6) are changed only by the caller.
- **Mirror every time:** `IONUT_APPLICATION_DESIGN_LANGUAGE.md` (full) → `IONUT_APPLICATION_DESIGN_LANGUAGE.SKILL.md` (condensed, same rules and job lines) → the `desktop-user-interface` copy → the repositories that carry it. A change that is not mirrored is not done.
- **Self-check after an edit:** contents and numbering match; every cross-reference (§n) points to the right section; §17 lists every rule.

- **Correct:** a new image-editor app adds "Image editor → canvas" (already present) and `panel.layers` to the id list, plus one checklist line.
- **Avoid:** rewriting the whole document in a new structure; editing only the .md and forgetting the skill.

**What this is NOT:** not version churn; not a changelog (history lives in source control).

---

## 17. Conformance checklist and closing

**If you are implementing this, your job is:** verify every item before shipping a screen or accepting an AI UI change, and report failures explicitly.

- [ ] Adoption question asked at project start and the answer recorded (§1).
- [ ] UX (interaction, focus, movement) **and** UI (visuals) both checked (§0).
- [ ] Every input-taking surface registers with an id `<kind>.<scope>[.<id>]` at its level (§2.6).
- [ ] Input and commands route to the active surface; listed commands bubble (§2.3, §2.7).
- [ ] Opening a surface moves focus in to the correct initial target; modals trap `Tab` (§2.5).
- [ ] Closing restores previous surface, control/trigger, caret, scroll; never stranded; never jumps to app-level controls (§2.5).
- [ ] Focus and selection are stored and shown separately (§2.4, §2.9).
- [ ] Toolbar clicks do not steal focus; each window has its own stack (§2.6).
- [ ] §2.6 tests exist and pass.
- [ ] Core function visually primary and active on window open (§3).
- [ ] No needed command, menu, or submenu removed (§3.1, §14).
- [ ] Heights 24/28/32; icons 16/20; UI type 12–14; spacing 4/8/12; targets ≥32/44 (§4).
- [ ] Hidden controls always reachable via menu, palette, or toolbar (§5).
- [ ] Dropdowns per §6; buttons and six states per §7.
- [ ] AA contrast, focus ring, full keyboard, screen-reader labels, reduced motion (§8).
- [ ] Vector-scalable at 100/150/200% (§9).
- [ ] Precise wording; surrounding controls quiet but reachable (§10).
- [ ] Spacing and nested menus organized (§11).
- [ ] Feedback in place, actionable, not stealing focus (§12).
- [ ] Tokens for color; AA in every theme; motion optional (§13).
- [ ] Hallmark Floor obeyed; product foundation (warm opaque) under Floor; Liquid Glass only on chrome; redesign/audit only when named (§15).
- [ ] Changes mirrored to the skill and desktop-user-interface copy (§16).

### Closing — this document stands alone

This document stands alone. An AI or developer reading **only this file plus the codebase** can implement every rule — adoption, focus levels, core function, the practical rules, the Hallmark visual floor, and maintenance — without any other design document or external guideline. Credited sources are already written out here in full. Where something cannot be implemented as written, fix this document (§16); do not improvise around it.

*Ionuț Application Design Language — design application language · design logic language — UX and UI rules for every application.*
