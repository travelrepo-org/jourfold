# Using Jourfold

## Your trips

Jourfold opens with your trip library. Each card shows the dates, the main places and whether the trip is only on this computer or shared. Right-click a card to show its folder or remove it from the list; removing a card never deletes the trip.

If you are new, choose **Explore a sample trip**. It creates "Spring in Japan" in your trips folder, clearly marked as a sample. Change it freely or delete the folder when you are done.

**New trip** asks for a title and nothing else. The optional steps add dates, the main timezone, your name and travel companions, where the folder is saved, and whether to share the trip right away: keep it on this computer, publish it to a new private GitHub repository, or connect an existing Git repository. **Create now** skips the rest. The default folder is `Documents/Jourfold`; change it in Settings under General.

**Open** also opens an existing trip folder, a trip someone shared as a Git address, or trips from your GitHub account.

## Planning

The sidebar switches between the parts of a trip. **Plan** shows the timetable. The buttons above it switch to the day-by-day **List** or the **Map**. On the map, the layers button sets **Labels** to show names when there is room, always, or never; with labels off you see only routes and symbols, and the selected place keeps its name.

- **Add** (Ctrl+N) opens Quick Add. Pick a type, give it a title and, if you like, a place, date and time. Typing a place name that does not exist yet creates it.
- Drag across empty time in the timetable to add an activity for exactly that span. Double-click empty time for a one-hour activity.
- Drag a block to move it, or drag its lower edge to change how long it takes. Alt+Up and Alt+Down move the focused block by 15 minutes.
- Right-click a block to change its time, duplicate it, move it back to the Inbox or delete it.
- **By person** shows one column per traveller. Shared plans appear in each person's column.

Colours and icons show what kind of plan an item is: transport, a stay, food, a sight, culture, nature and so on. Dashed outlines mean the time is not fixed: an idea, an approximate time or a part of the day. A check mark means the plan is confirmed; a warning triangle means something needs a look, such as two overlapping plans for the same person.

All-day plans appear in the strip above the hours.

Stays have their own band of **Nights** below the hours. A stay is one bar over all the nights it covers, from the check-in day to the day before check-out; thin lines in the grid mark check-in and check-out time. Drag a bar sideways, or use Alt+Left and Alt+Right, to move a stay by whole nights. The inspector shows the number of nights with buttons for one more or one less. Check-in and check-out decide the nights: if the stay's time says something else, the inspector explains it and **Fix the time** brings both in line. A night between the first and last day of the trip without a stay shows a quiet **No place to stay**, unless something else runs through that night, such as a night flight; click it to add a stay for that night. **Accommodation details** in the inspector hold the accommodation's own rules and practical facts: check-in hours (for example 15:00 to 20:00) and the latest check-out, how you get in (reception, self check-in with a key box or smart lock, or keys from the host), whether your arrival or departure time is still open, free cancellation until, breakfast, parking and arrival notes. A place's phone, e-mail and website appear there as buttons. With check-in hours set, the timetable shades them on the arrival day, and Jourfold warns when your planned arrival, or a journey that ends that day, is later than the last check-in. Do not write door or key box codes into the trip: it is shared and keeps its history. If you entered a stay night by night, **Merge stays** in the toolbar turns each run of nights at the same place into one stay, keeping notes, people and links. One **Undo** reverts it. Times are shown in the trip's main timezone. Items in another timezone also show their local time, for example the departure time of a flight. The timezone button above the timetable adds a second time column, for example the time at home, in the timetable and the list. It offers the timezones the trip already uses and this computer's timezone, and remembers the choice per trip.

In **Add**, transport takes departure and arrival in local time at each end, as printed on a ticket, and shows the travel time. Activities and stays use the timezone of their place; **Change** picks another one. Timezone fields find zones by city, country or offset, for example "China", "Shanghai" or "UTC+8".

A place's latitude and longitude are saved together once both are filled in; a point or a comma both work as decimal separator. Pasting coordinates copied from a map, such as `-37.0093, 174.7841`, into either empty field fills both.

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

Right-click any item in a list, a file, a person or a map pin for the same actions as in the inspector's menu, including **Delete**.

Deleting an item that nothing else refers to shows a short notice with **Undo** instead of asking first. When other items link to it, Jourfold lists them and removes the link, so deleting a place leaves the activities there without a place. Places, files, notes and bookings that only the deleted item used are offered for deletion as well; places, files and notes are preselected, bookings are not. Comments on the item go with it. One **Undo** brings everything back.

### Weather

Weather is off until you turn it on, from the **Weather** section of an activity or under **Settings**, **Weather**. Then each day of the timetable and the list shows the weather at the place where most of that day happens, or where you sleep. Activities show the forecast for their own place and time: hour by hour within a week, as a trend up to about two weeks ahead, and as climate averages beyond that, taken from the same days of the last ten years. Journeys show the weather at departure and arrival.

Jourfold estimates whether an activity is outdoors. A walk, a bike ride, nature and sightseeing count as outdoors; food, culture, shopping, nightlife, meetings and stays as indoors; general activities and free time as maybe outdoors. Car, bus, train and plane journeys only matter in severe weather. Some words in the title change the estimate, such as "hike", "beach" or "Wanderung" towards outdoors and "museum" or "caves" towards indoors. The **Weather** section says what Jourfold assumed and why. **Outdoors** or **Indoors** sets it for the item, and items in a group take the group's choice; **Automatic** goes back to the estimate. The choice is saved in the trip, so your travel companions see it too.

Within three days, weather that matters becomes a reminder: rain, strong wind, heat or frost for outdoor activities, only heavy rain, storms, thunderstorms or severe heat for activities that may be outdoors, and only severe weather for journeys, with a lower wind limit for ferries. Indoor activities never get a weather warning.

Open-Meteo is the default service. Under **Settings**, **Weather** you can choose MET Norway, the US National Weather Service, or Visual Crossing with your own API key, which is kept in the system's secure storage. Where the chosen service has no forecast, Jourfold uses Open-Meteo. Only coordinates rounded to about a kilometre are sent; titles, dates and names never are. Forecasts are kept on this computer for offline use and never written into the trip. A connected AI assistant can read these cached forecasts, but cannot make Jourfold download weather.

### Reminders

The bell next to the people in the header counts reminders: free cancellation deadlines within two weeks, tasks due today or overdue, arrivals after the last check-in, nights without a place to stay, bookings that are not confirmed shortly before they start and, with weather on, weather warnings. Urgent ones are marked in red and with the word **Urgent**. **Show** opens what the reminder is about, **Later** hides it until tomorrow and **Done** hides it for good. What you dismiss is remembered on this computer only; the trip is not changed. While Jourfold is open, urgent reminders also appear once as a notification of your system. Turn this off under **Settings**, **General**, **Desktop notifications**. Jourfold does not run in the background, so there are no reminders while it is closed.

## Bookings, costs, tasks and files

**Bookings** lists reservations with their reference (click the copy icon), price, travellers and status. Bookings and plans have a **Confirmations and files** section: drag a confirmation e-mail or PDF onto it, or choose a file. Thunderbird, eM Client and Apple Mail let you drag an e-mail directly; Gmail and Outlook on the web download it as an `.eml` file first, and classic Outlook can print it to PDF. The help button next to the area explains each. Click a confirmation to open it in Jourfold's document window, which stays open beside the plan. It shows PDFs page by page with zoom and search (Ctrl+F), and e-mails with sender, date, attachments and the message in its original layout; **Text** shows the plain text instead, and Jourfold remembers your choice. An attachment can be opened there or added to the trip next to the e-mail. Scripts, forms and frames in an e-mail are removed, and images from the internet are only loaded when you choose **Load images** for that e-mail, because loading them tells the sender that you opened it. Links ask before they open and show where they really go. **Costs** totals expenses per currency and compares them with budgets. Amounts are never converted with current exchange rates. **Tasks** works like a checklist; type a task and press Enter. Packing lists have their own page (see below). **Files** shows attached tickets, PDFs and photos. Drop files anywhere on the window to add them.

### Packing lists

**Packing lists** holds what to bring, in as many lists as you like, for example one for the barbecue and one for the camping gear. Type an item and press Enter. In the same line, `@name` chooses who brings it, `@all` means everyone, and `#group` puts it in a group: "Grill @Sam #Cooking". Items without a group are listed under **Other**.

Several people chosen for an item bring it together, so one of them packing it is enough. **Everyone** means each traveller brings their own, including people added to the trip later. Click a person's chip to mark the item as packed or not packed for them, so one person can keep the list for everyone; your own items also have a check box. **Mine** shows only what you bring, and **Nobody yet** shows what nobody has taken. **Who brings it?** and **Group and quantity** are in the item's menu and in the details panel. Deleting a list deletes its items too, unless they are on another list, and **Undo** brings everything back.

Packing lists are saved in the trip like everything else. Older versions of Jourfold show them as a collection of tasks.

### Who you are in a trip

Jourfold needs to know which person you are for **Mine** and for your own check boxes. It recognises you when your GitHub account or the e-mail under **Settings**, **You** is stored on a person of the trip. When someone else added you before you joined, the bell asks once **Who are you in this trip?**: pick yourself, add yourself as a new person, or say you are not part of the trip. You can also open a person and choose **This is me**. Your choice is remembered on this computer only. Optionally, Jourfold saves your e-mail and GitHub name on that person, so the history shows your versions under your name and your other computers recognise you; everyone who can open the trip sees them, and Jourfold warns when the person already has someone else's.

## Versions, variants and sharing

Your changes are saved on your computer immediately. The line under the trip title says whether they are already part of a **version**. **Create version** (Ctrl+S) records the current plan in the trip history with a suggested description that you can edit. **History** lists all versions, shows what changed and can restore an earlier one.

A **variant** is an alternative plan, for example "Cheaper hotels". Use the variant button next to the title to switch, or **Variants** to create, compare and merge them. Comparison shows the two plans day by day. When both changed the same detail, Jourfold asks once which result to keep.

**Variants** shows each variant once, whether it is on this computer, online or both. Labels say where it lives (online, only on this computer, only online) and a line says how it differs from its online copy: the same, versions not uploaded yet, new versions online, or changed on both sides. **Sync** on a variant that is not open brings it up to date without switching, unless it changed on both sides. A note says when the main plan has newer versions; **Get changes from the main plan** in the open variant brings them in.

When a variant is ready, **Take over into main plan** merges it into the main plan, with the usual questions when both changed the same detail. The variant is then marked as taken over and Jourfold offers to delete it, online too. **Delete** (in the menu of a variant) keeps deleted variants restorable for 30 days under **Deleted**; for variants that were not taken over, deleting the online copy is an extra choice. The main plan and the open variant cannot be deleted. **Archive** still hides a variant without deleting it.

When someone deletes a variant online that you still have, the bell shows **Variants deleted online**. **Clean up** lists them: unchanged copies are suggested for deletion, copies with your own versions and the open variant are kept unless you choose otherwise. Kept copies stay only on this computer and are not uploaded again, so they do not come back for the others; **Share again** puts one online once more.

**Share** depends on where the trip lives:

- On this computer only: publish to a new private GitHub repository, or connect any Git server.
- On GitHub: invite people by username and copy an invitation link.
- On another Git server: copy an invitation link. The other person needs access on that server; a link does not grant it.

Shared trips sync in the background. Changes that fit together are merged after a short notice that offers **Review** and **Cancel**.

When sync or another Git step fails, Jourfold says what happened in plain words and offers a fix where it can make one safely, for example removing a lock file left by a stopped Git program, moving files that are in the way into a separate folder, or getting newer versions first. **Technical details** shows the Git command, its output with sign-in data and your home folder removed, and the state of the trip folder. **Copy details** puts that text on the clipboard for a bug report; nothing is sent. Files in the trip folder that are not part of the trip, such as your own notes, no longer count as unsaved changes.

## Search and keyboard

Ctrl+K opens search and commands. It finds items in the trip and runs actions such as creating a version or switching views. Alt+1 to Alt+9 jump between the main sections. Escape closes the details panel or a dialog. All buttons and fields are reachable with Tab.

## Settings and privacy

Settings covers language (English and German), theme, density, text size, high contrast, units, your name for versions, the folder for new trips, online maps, weather, GitHub and plugins. Under **Sharing**, the GitHub card shows which account is connected, links to its profile and can disconnect it. **Advanced mode** adds Git branch names, commit details and the stored data of each item.

Online maps are off until you enable them. When on, Jourfold loads map images for the area you look at and sends the address searches you start to the map servers, OpenStreetMap unless you enter others under **Map servers**. It also checks Jourfold's published list of map servers once a day. Your trip data is never sent. Weather is off until you turn it on as well; it sends only rounded coordinates (see [Weather](#weather)). Jourfold has no analytics and sends nothing automatically. **Report a problem** (under **Advanced**, and **Report** in every error message) prepares a report with technical details. You review and edit it, then copy it or choose **Send by e-mail**: your own mail program opens with the report addressed to the project, and you send it yourself. To find that address, Jourfold asks GitHub for the project's public contact address; a long report goes through the clipboard.

## Planning with an AI assistant

AI assistants that support the Model Context Protocol (MCP), such as LM Studio, Claude Desktop or Cursor, can read a trip and change it with you. Open **Connect an AI assistant** from the trip menu, the menu next to **Add**, or Ctrl+K. It shows an entry for the assistant's MCP settings; **Copy configuration** copies it. The assistant then starts Jourfold in the background as a server for this one trip. Tick **Read only** if the assistant should only look.

The assistant's changes are checked like your own, so it cannot leave the trip in a broken state, and they appear in Jourfold within a few seconds. They are not part of a version until you create one, and the assistant only creates versions when you ask it to. Jourfold itself sends nothing; a local assistant keeps the trip on your computer, while an online assistant sends what it reads to its provider.

Assistants without MCP can edit the trip folder directly. **Save agent guide** saves a description of the file format and the editing rules that you can give to such an assistant. [Connecting a local AI assistant](ai-assistants.md) walks through the setup with LM Studio.

## About Jourfold

**About Jourfold** (click the version next to the logo, use Settings, or search for it with Ctrl+K) shows the version with its code name and build, the TravelRepo version and trip format, links to the source code, the licenses of Jourfold and TravelRepo, the plugins you have loaded with their licenses and websites, and the open-source components Jourfold includes. **Copy details** copies the versions for a bug report.

## Exporting

The menu next to **Add** has **Export**. Choose **Trip plan** or **Packing lists**, then the format. **Copy as text** puts the same content on the clipboard, ready to paste into a message or an e-mail.

A trip plan can be a PDF, a web page for printing, a calendar file for other calendar apps, or plain text. You choose:

- **For**: everyone, or one person. One person's plan leaves out what only others do; things nobody is assigned to stay in.
- **Days**: the whole trip or a few days. Plans without a time are only included for the whole trip.
- **Contents**: schedule, stays, bookings, contacts, costs, tasks, notes and packing lists. In one person's plan, costs show what that person paid.
- **Map overview** (PDF): the places and routes of the export. With the street map turned on, the PDF uses its map images; otherwise it draws the places on a plain grid. A long flight from home runs off the edge of the map, so the trip itself stays readable.
- **Attach confirmations** (PDF): PDFs, images and e-mails attached to the plans and bookings are added at the end as A1, A2 and so on, and each entry says which ones belong to it. Files that cannot go into a PDF are listed by name.
- **Detail**: detailed adds people, status, notes, house rules and contacts; compact keeps times, places and booking references.
- **Paper** and **Language**. The language changes only the labels; trip content is never translated.

Packing lists can be a PDF to tick off, plain text with check boxes, or a CSV file for Excel, LibreOffice Calc or Numbers. Export everyone's list in one file, one file per chosen person (Jourfold asks for a folder and names the files after each person), or only what nobody brings yet. With several lists you choose which ones. Jourfold remembers your last choices for each trip.

This build is under acceptance validation; see [acceptance status](acceptance.md) for remaining limitations.
