# Using Jourfold

## Your trips

Jourfold opens with your trip library. Each card shows the dates, the main places and whether the trip is only on this computer or shared. Right-click a card to show its folder or remove it from the list; removing a card never deletes the trip.

If you are new, choose **Explore a sample trip**. It creates "Spring in Japan" in your trips folder, clearly marked as a sample. Change it freely or delete the folder when you are done.

**New trip** asks for a title and nothing else. The optional steps add dates, the main timezone, your name and travel companions, where the folder is saved, and whether to share the trip right away: keep it on this computer, publish it to a new private GitHub repository, or connect an existing Git repository. **Create now** skips the rest. The default folder is `Documents/Jourfold`; change it in Settings under General.

**Open** also opens an existing trip folder, a trip someone shared as a Git address, or trips from your GitHub account.

## Planning

The sidebar switches between the parts of a trip. **Plan** shows the timetable. The buttons above it switch to the day-by-day **List** or the **Map**.

- **Add** (Ctrl+N) opens Quick Add. Pick a type, give it a title and, if you like, a place, date and time. Typing a place name that does not exist yet creates it.
- Drag across empty time in the timetable to add an activity for exactly that span. Double-click empty time for a one-hour activity.
- Drag a block to move it, or drag its lower edge to change how long it takes. Alt+Up and Alt+Down move the focused block by 15 minutes.
- Right-click a block to change its time, duplicate it, move it back to the Inbox or delete it.
- **By person** shows one column per traveller. Shared plans appear in each person's column.

Colours and icons show what kind of plan an item is: transport, a stay, food, a sight, culture, nature and so on. Dashed outlines mean the time is not fixed: an idea, an approximate time or a part of the day. A check mark means the plan is confirmed; a warning triangle means something needs a look, such as two overlapping plans for the same person.

Hotel stays and all-day plans appear in the strip above the hours. Times are shown in the trip's main timezone. Items in another timezone also show their local time, for example the departure time of a flight.

### The Inbox

The **Inbox** collects things without a time: ideas, notes and files you have not attached anywhere. Type an idea into the box at the top and press Enter. Drag an entry onto the timetable to schedule it. **Pin** keeps the Inbox open. Ctrl+I toggles it.

### Details

Selecting anything opens the details panel on the right. Changes are saved as soon as you leave a field. Everything can be undone with Ctrl+Z and redone with Ctrl+Shift+Z.

- **When** opens the time editor. Besides exact times you can choose an approximate time, a time window, a part of the day, all day, or no time yet. Daylight-saving gaps are explained; repeated hours ask which one you mean.
- **Status**, **Category** and **Who** are one click each.
- Transport has a mode, from and to places, carrier, number, seat, terminal and distance. Stays have check-in and check-out, guests and rooms.
- **Steps** groups several items, such as the parts of a day trip. Steps can share the group's people.
- **Notes** holds Markdown notes, links, attached files and links to other items.
- **Comments** lets travel companions leave remarks under their own name.

Deleting shows a short notice with **Undo** instead of asking first.

## Bookings, costs, tasks and files

**Bookings** lists reservations with their reference (click the copy icon), price, travellers and status. **Costs** totals expenses per currency and compares them with budgets. Amounts are never converted with current exchange rates. **Tasks** works like a checklist; type a task and press Enter. **Files** shows attached tickets, PDFs and photos. Drop files anywhere on the window to add them.

## Versions, variants and sharing

Your changes are saved on your computer immediately. The line under the trip title says whether they are already part of a **version**. **Create version** (Ctrl+S) records the current plan in the trip history with a suggested description that you can edit. **History** lists all versions, shows what changed and can restore an earlier one.

A **variant** is an alternative plan, for example "Cheaper hotels". Use the variant button next to the title to switch, or **Variants** to create, compare and merge them. Comparison shows the two plans day by day. When both changed the same detail, Jourfold asks once which result to keep.

**Share** depends on where the trip lives:

- On this computer only: publish to a new private GitHub repository, or connect any Git server.
- On GitHub: invite people by username and copy an invitation link.
- On another Git server: copy an invitation link. The other person needs access on that server; a link does not grant it.

Shared trips sync in the background. Changes that fit together are merged after a short notice that offers **Review** and **Cancel**.

## Search and keyboard

Ctrl+K opens search and commands. It finds items in the trip and runs actions such as creating a version or switching views. Alt+1 to Alt+9 jump between the main sections. Escape closes the details panel or a dialog. All buttons and fields are reachable with Tab.

## Settings and privacy

Settings covers language (English and German), theme, density, text size, high contrast, units, your name for versions, the folder for new trips, online maps, GitHub and plugins. Under **Sharing**, the GitHub card shows which account is connected, links to its profile and can disconnect it. **Advanced mode** adds Git branch names, commit details and the stored data of each item.

Online maps are off until you enable them. When on, Jourfold loads map images for the area you look at and sends the address searches you start to OpenStreetMap. Your trip data is never sent. Jourfold has no analytics and sends nothing automatically. **Report a problem** prepares a text you can review and copy.

## About Jourfold

**About Jourfold** (in Settings, or search for it with Ctrl+K) shows the version with its code name and build, the TravelRepo version and trip format, links to the source code, the licenses of Jourfold and TravelRepo, the plugins you have loaded with their licenses and websites, and the open-source components Jourfold includes. **Copy details** copies the versions for a bug report.

## Exporting

The menu next to **Add** has **Export**: a day-by-day PDF, a calendar file for other calendar apps, or a web page for printing.

This build is under acceptance validation; see [acceptance status](acceptance.md) for remaining limitations.
