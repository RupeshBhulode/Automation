# Two-Way Sync: Google Sheets ↔ Trello

An ASP.NET Core-based automation that keeps lead data synchronized between Google Sheets (Lead Tracker) and Trello (Work Tracker) in real-time.

---

## Overview

This project creates a bidirectional sync between:
- **Google Sheets** - Acts as our Lead Tracker where we store lead information (name, email, status, source, notes)
- **Trello** - Acts as our Work Tracker where we manage tasks related to each lead

The sync runs continuously, ensuring that changes in either system are reflected in the other within seconds.

### What It Does

- When you add or update a lead in Google Sheets → A Trello card is automatically created/updated
- When you move a Trello card between lists → The lead status in Google Sheets updates automatically
- When you edit card details in Trello → The corresponding lead data in Google Sheets is updated
- Handles edge cases like archiving cards, changing statuses to "LOST", and prevents duplicate entries

---

## Architecture & Flow

<img width="821" height="463" alt="image" src="https://github.com/user-attachments/assets/33fb475c-f909-46b6-ab5c-0761ce762b5c" />

### Status Mapping

**Google Sheets → Trello:**
- `new` → `TODO` list
- `contacted` → `INPROGRESS` list
- `qualified` → `DONE` list
- `lost` → Card is archived (removed from board)

**Trello → Google Sheets:**
- `TODO` list → `new` category
- `INPROGRESS` list → `contacted` category
- `DONE` list → `qualified` category
- Card archived/deleted → `LOST` category

---

## Setup Instructions

### Prerequisites
- .NET 8.0 SDK or higher
- A Google account
- A Trello account (free)
- Visual Studio 2022 or Visual Studio Code (optional)

### Step 1: Clone the Repository
```bash
git clone automation-two-way-sync-Rupesh-Bhulode
cd automation-two-way-sync
```

### Step 2: Restore Dependencies
```bash
dotnet restore
```

The project uses the following NuGet packages:
- `Google.Apis.Sheets.v4`
- `Google.Apis.Auth`
- `Newtonsoft.Json`
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Configuration`

### Step 3: Set Up Google Sheets

1. **Create a Google Sheet:**
   - Go to [Google Sheets](https://sheets.google.com)
   - Create a new spreadsheet
   - Add headers in the first row: `id`, `name`, `email`, `category`, `note`, `source`
   - Add some sample data (category should be: new, contacted, qualified, or lost)

2. **Enable Google Sheets API:**
   - Go to [Google Cloud Console](https://console.cloud.google.com)
   - Create a new project or select an existing one
   - Enable the "Google Sheets API"
   - Go to "Credentials" → "Create Credentials" → "Service Account"
   - Download the JSON key file and save it as `credentials.json` in your project root
   - Copy the service account email (looks like `xxx@xxx.iam.gserviceaccount.com`)
   - Share your Google Sheet with this email address (give it Editor access)

3. **Get your Sheet ID:**
   - From your Google Sheet URL: `https://docs.google.com/spreadsheets/d/SHEET_ID_HERE/edit`
   - Copy the `SHEET_ID_HERE` part

### Step 4: Set Up Trello

1. **Create a Trello Board:**
   - Go to [Trello](https://trello.com)
   - Create a new board (any name you like)
   - The sync will automatically create three lists: TODO, INPROGRESS, DONE

2. **Get API Credentials:**
   - Visit [Trello API Key](https://trello.com/app-key)
   - Copy your API Key
   - Click on "Token" link to generate a token
   - Copy the Token

3. **Get Board ID:**
   - Open your Trello board
   - Add `.json` to the end of the URL and press Enter
   - Look for `"id":` near the top - that's your board ID

### Step 5: Configure Application Settings

Update your `appsettings.json` file:

```json
{
  "GoogleSheets": {
    "SheetId": "your_sheet_id_here",
    "CredentialsFile": "credentials.json"
  },
  "Trello": {
    "ApiKey": "your_trello_api_key",
    "Token": "your_trello_token",
    "BoardId": "your_board_id"
  },
  "Sync": {
    "DataJsonPath": "data.json",
    "PollIntervalSeconds": 5
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

**⚠️ IMPORTANT:** Never commit `credentials.json` or API keys to Git! Use `appsettings.Development.json` for local settings and add it to `.gitignore`. For production, use environment variables or Azure Key Vault.

---

## Usage

### Running the Sync

Using .NET CLI:
```bash
dotnet run
```

Or if using Visual Studio:
- Press F5 or click "Start" button

The application will:
1. Connect to both Google Sheets and Trello
2. Perform an initial sync
3. Continue running as a background service, checking for changes every 5 seconds (configurable via `PollIntervalSeconds`)

### Testing the Sync

**Test Sheet → Trello:**
1. Open your Google Sheet
2. Add a new row: `1 | Rupesh Bhulode | r@gmail.com | new | Test lead | Website`
3. Wait 5 seconds
4. Check Trello - you should see a new card "Rupesh Bhulode (LeadID: 1)" in the TODO list

**Test Trello → Sheet:**
1. Open your Trello board
2. Drag a card from TODO to INPROGRESS
3. Wait 5 seconds
4. Check Google Sheets - the category should change from "new" to "contacted"

**Test Field Updates:**
1. Edit a Trello card's description (change email, note, or source)
2. Wait 5 seconds
3. Check Google Sheets - the corresponding fields should update

**Test Archiving:**
1. In Google Sheets, change a lead's category to "lost"
2. Wait 5 seconds
3. The Trello card should be archived (disappear from the board)

### Stopping the Sync

Press `Ctrl+C` to gracefully stop the application.

---

## Project Structure

```
.
├── Services/
│   ├── LeadService.cs          # Google Sheets API service
│   ├── TaskService.cs          # Trello API service
│   ├── SyncService.cs          # Core sync logic (bidirectional)
│   └── SyncBackgroundService.cs # Hosted service with polling loop
├── Models/
│   ├── Lead.cs                 # Lead data model
│   ├── TrelloCard.cs           # Trello card model
│   └── SyncState.cs            # State management model
├── Configuration/
│   ├── GoogleSheetsConfig.cs   # Google Sheets settings
│   ├── TrelloConfig.cs         # Trello settings
│   └── SyncConfig.cs           # Sync settings
├── Program.cs                  # Application entry point
├── appsettings.json            # Configuration file
├── data.json                   # State file (auto-generated)
├── credentials.json            # Google service account key (gitignored)
└── README.md                   # This file
```

---

## How It Works

### Idempotency & Deduplication

The system maintains a mapping file (`data.json`) that tracks:
- Which lead ID corresponds to which Trello card ID
- The last known state of each lead/card

This ensures:
- Running the sync multiple times won't create duplicate cards
- Updates are only made when actual changes occur
- The system can recover gracefully after crashes

### Error Handling

- **API failures:** Each API call is wrapped in try-catch blocks with logging via `ILogger`
- **Rate limiting:** The system uses polling with configurable intervals to avoid hitting rate limits
- **Bad data:** Empty or invalid records are skipped with warnings logged
- **Network issues:** Errors are logged but don't crash the entire sync process

### Dependency Injection

The application uses ASP.NET Core's built-in dependency injection:
- Services are registered in `Program.cs`
- Configuration is injected via `IOptions<T>`
- Logging is provided through `ILogger<T>`
- Background service runs as an `IHostedService`

### Logging

All operations are logged using the built-in logging framework:
- `Information`: Normal operations (card created, field updated, etc.)
- `Error`: Failed operations with details
- `Critical`: Critical errors with full stack traces

Check the console output to see what's happening in real-time. Logs can be configured to write to files, Application Insights, or other providers.

---

## Assumptions & Limitations

### Assumptions Made
- Lead IDs in Google Sheets are unique and don't change
- Category values are lowercase: "new", "contacted", "qualified", "lost"
- The Google Sheet has headers: id, name, email, category, note, source
- Users won't manually delete cards from Trello without updating the sheet

### Known Limitations
- **Polling-based:** Uses polling instead of webhooks (Trello webhooks require a publicly accessible endpoint)
- **Single sheet:** Only syncs the first worksheet in the spreadsheet
- **No conflict resolution:** If both systems are updated simultaneously, last-write-wins
- **Manual Trello deletions:** If you manually delete a card from Trello, the lead will be marked as "LOST" on next sync
- **Rate limits:** Google Sheets has rate limits - sync interval should not be too aggressive

### Not Implemented (Due to Time)
- Real-time webhooks (would require hosted API endpoint)
- Conflict detection and resolution
- Batch updates (currently one-by-one)
- Historical change tracking
- User authentication/multi-user support
- Undo functionality
- Web UI dashboard

---

## Deployment Options

### Running as Windows Service
```bash
dotnet publish -c Release
sc create SyncService binPath="path\to\YourApp.exe"
sc start SyncService
```

### Running in Docker
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY bin/Release/net8.0/publish/ .
ENTRYPOINT ["dotnet", "YourApp.dll"]
```

### Deploying to Azure
- Azure App Service (Web App)
- Azure Container Instances
- Azure Kubernetes Service (AKS)
- Azure Functions (with timer trigger)

---

## AI Usage Notes

### Tools Used
- **ChatGPT (GPT-4)** - For code structure planning, API documentation clarification, and debugging

### Where AI Helped
1. **API Documentation Summary:** Used ChatGPT to quickly understand Trello and Google Sheets API endpoints instead of reading lengthy docs
2. **Service Pattern:** Generated initial service structure following ASP.NET Core best practices
3. **Error Handling Patterns:** Got suggestions for try-catch structure and logging in services
4. **Configuration Binding:** Generated IOptions pattern implementation

### What I Changed/Rejected
**Example:** ChatGPT initially suggested using Entity Framework with SQL Server for state management instead of `data.json`. I rejected this because:
- Added unnecessary complexity for a small project
- JSON is human-readable and easier to debug
- No concurrent access concerns with single-threaded polling
- Keeps dependencies minimal

I also modified AI-generated logging statements to use structured logging with proper log levels.

---

## Video Demo
**[📹 Watch the Demo Video](youtube.com/watch?v=zyVXoDrOW2w&feature=youtu.be)**

The video covers:
- Quick architecture overview
- Code walkthrough of key components
- Setup process demonstration
- Live sync demo (Sheet → Trello and Trello → Sheet)

---

## Troubleshooting

**"Failed to authenticate with Google Sheets"**
- Make sure `credentials.json` is in the project root
- Verify you've shared the sheet with the service account email
- Check that the credentials file path in `appsettings.json` is correct

**"Trello list not found"**
- The application auto-creates lists - make sure the board ID is correct
- Check that your API key and token are valid
- Verify the Trello configuration in `appsettings.json`

**"Cannot find the specified file"**
- Run `dotnet restore` to restore NuGet packages
- Check that all required files are present

**Sync is slow or not happening**
- Check the `PollIntervalSeconds` in `appsettings.json` (lower = faster, but watch rate limits)
- Look at console logs for errors
- Ensure the application is running (check with `dotnet run`)

---

## Future Enhancements

If I had more time, I'd add:
- Webhook support for real-time updates using ASP.NET Core Web API
- Blazor web dashboard to view sync status
- Conflict resolution strategy with user notifications
- Support for custom field mappings via configuration
- Batch API calls for better performance
- SignalR for real-time UI updates
- Comprehensive unit and integration tests using xUnit
- Health checks and metrics endpoints
- Azure Application Insights integration

---

## License

This is a take-home assignment project. Feel free to use it for learning purposes.

---

## Contact

If you have questions about the implementation, please reach out via GitHub issues or the interview process.

Happy syncing! 🚀
