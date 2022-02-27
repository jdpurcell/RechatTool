# RechatTool
Command line tool to download the chat log from a Twitch VOD. Saves the full JSON data and optionally processes it to produce a simple text file. Requires .NET Framework 4.6.2+ (releases labeled Windows), or .NET 6 (releases labeled CrossPlatform).

Sample usage (Windows):
```
RechatTool -D 111111111
```
Sample usage (Other platforms with .NET 6 runtime installed):
```
dotnet RechatTool.dll -D 111111111
```
Downloads the chat replay for video ID 111111111 and saves the .json and processed .txt output in the current directory.

Run without any arguments to see full list of modes.
