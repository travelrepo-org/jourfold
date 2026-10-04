# Connecting a local AI assistant

Jourfold can serve an open trip to an AI assistant through the Model Context Protocol (MCP). This guide shows how to connect an assistant that runs on your own computer, so the trip does not leave it.

## What you need

- Jourfold 0.3.0 or newer with the trip you want to plan.
- An assistant app that supports MCP servers, for example [LM Studio](https://lmstudio.ai/) 0.3.17 or newer.
- A model that can call tools. Recent models such as Qwen3, Llama 3.1 or newer, Mistral Small or gpt-oss work; LM Studio marks them with a tool icon. Give the model a context length of at least 16k tokens, because the trip format guide and the schedule take some room.

## 1. Copy the configuration from Jourfold

1. Open the trip in Jourfold.
2. Choose **Connect an AI assistant** in the trip menu (click the trip name at the top left), in the menu next to **Add**, or search for it with Ctrl+K.
3. Tick **Read only** if the assistant should only look at the trip.
4. Click **Copy configuration**.

The configuration names this Jourfold program and the trip folder, for example:

```json
{
  "mcpServers": {
    "jourfold-spring-in-japan": {
      "command": "/home/alex/.local/share/jourfold-app/Jourfold.Desktop",
      "args": ["--mcp", "/home/alex/Documents/Jourfold/Spring in Japan"]
    }
  }
}
```

Each entry serves one trip. Repeat the steps for every trip the assistant should see. If you move Jourfold or the trip folder, copy the configuration again.

## 2. Add it to the assistant

### LM Studio

1. Open the **Program** tab in the right sidebar and choose **Install**, then **Edit mcp.json**.
2. If the file is still empty, replace its content with the copied configuration. If it already lists other servers, copy only the `"jourfold-…": { … }` entry into the existing `mcpServers` block and keep a comma between the entries.
3. Save the file. LM Studio starts the server and shows `jourfold-…` with its tools in the **Program** tab. Turn it on for the chat if it is not on yet.
4. Load a model that can call tools and start a new chat.

### Other assistants

Most other MCP clients, such as Claude Desktop, Cursor or Jan, read the same `mcpServers` format from their settings file. Add the copied entry there and restart the assistant if it does not pick up the change on its own. For clients with a form instead of a file, enter the `command` as the program and the two `args` (`--mcp` and the trip folder) as arguments.

## 3. Plan together

Start with a short request so the assistant looks at the trip first, for example:

> Read the guide of the Jourfold server, then show me what is planned for 15 May and suggest two restaurants near the hotel as ideas.

The assistant reads the trip, the schedule and the format guide through the server's tools. Many apps ask you to confirm each tool call; you can allow the reading tools permanently and keep confirmations for changes.

The assistant's changes appear in Jourfold within a few seconds. They are checked like your own, so a wrong time or an unknown timezone is refused instead of saved. The changes are not part of a version until you create one in Jourfold, or until you ask the assistant to save a version.

## When something does not work

- **The server does not start or shows no tools.** Copy the configuration again from Jourfold; the paths must point to the installed Jourfold and an existing trip folder. On Linux, test the command in a terminal: `Jourfold.Desktop --mcp "/path/to/trip"` should wait silently for input (end it with Ctrl+C). It prints a message and stops if the folder is not a trip.
- **The model answers without using the tools.** Choose a model with tool support and a larger context length, and ask it explicitly to use the Jourfold tools.
- **A change is refused.** The reason is in the tool result, for example `time.start.local`. Most models correct themselves when they read it; otherwise point the assistant to it.
- **You want the assistant to stop changing things.** Turn the server off in the assistant, or replace its entry with the read-only configuration.

Jourfold itself sends nothing to any service. With an online assistant instead of a local one, what the assistant reads from the trip goes to that assistant's provider.

The server and its tools are described in more detail in the TravelRepo documentation (`docs/mcp.md` in the TravelRepo repository).
