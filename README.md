# AI Place Guide

Click anywhere on the map, or search for a city, country or place, to see its "cube" and get an AI-generated visitor guide in English or Italian, with audio.

This is a small weekend prototype inspired by Cubish's idea of connecting digital content to physical places.

## Features

- Interactive dark map (Leaflet + OpenStreetMap)
- Search for any city, country or place
- "Use my location" button
- A 100 m x 100 m "cube" grid drawn on the map around the selected spot
- Place name, city and country for any clicked point
- AI visitor guide in English or Italian, based on nearby Wikipedia articles
- Listen button (browser text-to-speech)

## Tech stack

- ASP.NET Core minimal API (C#)
- Leaflet + OpenStreetMap
- Wikipedia API for facts
- Nominatim for search and reverse lookup
- Google Gemini API for the guide text
- Plain HTML, CSS and JavaScript on the front end

## How it works

1. You click on the map or search for a place.
2. The frontend calculates the cube for that point and shows it on the map.
3. The C# backend finds Wikipedia articles within 10 km of the point and reads their summaries.
4. The summaries are sent to Gemini, which writes a short guide using only those facts.
5. The guide appears in the side panel and can be read aloud.

## Run locally

1. Install the .NET SDK (or Visual Studio with the "ASP.NET and web development" workload).
2. Get a free API key from [Google AI Studio](https://aistudio.google.com).
3. Add your key with user secrets so it never ends up in the repo:

````
dotnet user-secrets init
dotnet user-secrets set "Gemini:ApiKey" "YOUR_KEY"
dotnet user-secrets set "Gemini:Model" "gemini-3.5-flash-lite"
````

   In Visual Studio you can also right-click the project and choose **Manage User Secrets**.
4. Run the project (F5, or `dotnet run`) and open the local address shown in the console.

Model names change over time. If you get a "model not found" error, check Google AI Studio for a currently available Gemini model and update `Gemini:Model`.

## Notes

- Guides depend on Wikipedia coverage, so well-known places return richer results than small streets or villages.
- The cube grid is my own simple prototype. It is not Cubish's actual system.
- The AI is told to use only the Wikipedia facts it is given, but always double-check important details.

## Credits

- Idea inspired by [Cubish](https://cubish.com)
- Map data (c) OpenStreetMap contributors
- Facts from Wikipedia

Built by Shajjar Abbas.
