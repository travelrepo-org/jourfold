# Planning a trip

Choose **New trip**, enter a title, and select an empty folder for the plan and its history. Optional dates use calendar pickers; content language and timezone have searchable choices. The participant list starts empty. Use **Add participant**, enter a name and choose Save or Cancel. The trip stays on your computer until you add a remote or publish it. Optional trip settings are available by selecting **Details** beside the planning controls.

Use **Quick add** to create an activity, transport, accommodation, task, note, booking, place, person, expense or collection. Changes in the inspector are written when you leave a field. Use **Undo** and **Redo** through Ctrl+Z and Ctrl+Shift+Z. Changes made outside Jourfold invalidate undo history and show a notice.

Unscheduled activities appear in **Inbox**. Drag one onto the timetable or use **Date and time** in its inspector. A block can be moved by dragging and resized by dragging its lower edge. Exact times use the timezone shown beside them. The date/time form uses calendar and time pickers and lets you review both endpoints together. Repeated daylight-saving times offer a choice of UTC offsets; nonexistent times show an explanation.

**Create version** records a named checkpoint. Review the change summary and edit the suggested message. Use **Variants** to try another plan, compare changes and merge it back. Create a version before switching variants. Merged variants remain archived.

**Files** attaches and previews image documents. Other files can be opened in the operating system's viewer. Large attachments require confirmation. **Export** creates PDF, calendar or print-friendly HTML output.

**Map** shows locally stored coordinates and transport connections. Add coordinates to a place to include it. It works without a map service or API key.

Ctrl+K searches trip content and commands. **Settings** contains language, theme, density, units, local identity, provider connection and diagnostics. English and German affect the interface; trip content is not translated.

A copied repository address does not grant another person access. GitHub invitations require permission to manage that repository. New GitHub repositories are private by default.

This build is under acceptance validation. Consult `docs/acceptance.md` for remaining limitations before using it as a release.

## People, material and comparisons

In People, **Add myself to this trip** creates or reuses a trip-local person from your local name/email template. Repeating the action maps to the same Git email within the trip. This does not create an online account.

The Inbox contains unscheduled activities and unlinked notes/documents. Pin keeps it open across sessions. Drag a note or file into the plan to create a scheduled activity linked to that material. The original note/file remains intact. **Schedule in plan** in the inspector provides the keyboard equivalent. Use the time fields or precision action to change timing without dragging.

In Variants, **Compare** opens a timetable comparison and a separate side-by-side detail view. The timetable uses the current trip's timezone for both plans. Changed Markdown and binary choices are included in the details. **Share variant** copies a clone link with the branch context; the recipient still needs server access, and the branch must have been pushed.

Markdown sections can be added, edited and removed in the inspector. Document import, document replacement and Markdown edits participate in local Undo/Redo. Image previews are local. PDFs and other supported files can be opened with the operating system's default application.

History shows the author and application activity. Expand Details for semantic changes. Advanced Mode adds hashes, refs, author/committer identities, trailers and parent information. Restoring a version shows the files that will change and leaves the restored content as local changes, ready for a new version.

## Readability

Settings includes high contrast and text scaling. Platform scaling and theme preferences are also respected. Text scaling can introduce scrollbars on small windows so controls remain reachable. The app uses no custom animated transitions. Keyboard focus and textual warnings do not depend on color alone.

## Editing and local copies

Each content view has an Add action in its toolbar. Quick add remains available for all entity types. In Plan, right-click an empty time slot to add an activity, or drag over an empty span to prefill its start and end. Right-click a block to remove it after confirmation. Zoom has minus/plus controls and a percentage list.

The participant editor shows available people on the left and assigned people on the right. Select people and use the arrows, then Save to apply the whole selection or Cancel to leave it unchanged.

**Save as…** copies the current repository to an empty folder and opens the copy. It includes current local edits, attachments, untracked files, history, variants, local Git settings and remotes. The original remains unchanged. Because remotes are copied too, the copy can still sync to the same remote. Linked Git worktrees, shared object stores and symbolic links are rejected with an explanation rather than copied incompletely.

Settings has a category sidebar. General and Appearance controls apply when changed. Identity stores your local name/email template. GitHub connection and discovery are under Connections. Application forms stay inside the main window; file and folder selection uses the operating system's picker.
