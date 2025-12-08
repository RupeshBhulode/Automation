using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Configuration;


namespace DotNet2;

public class GoogleSheetClient
{
    private readonly SheetsService _service;
    private readonly string _spreadsheetId;
    private const string SheetName = "Sheet1";

    public GoogleSheetClient(IConfiguration configuration)
    {
        var credentialsPath = configuration["Google:CredentialsFile"];
        _spreadsheetId = configuration["Google:SpreadsheetId"];

        if (string.IsNullOrEmpty(credentialsPath) || string.IsNullOrEmpty(_spreadsheetId))
        {
            throw new InvalidOperationException("Google credentials and spreadsheet ID must be configured");
        }

        var credential = GoogleCredential.FromFile(credentialsPath)
    .CreateScoped(SheetsService.Scope.Spreadsheets);

    _service = new SheetsService(new BaseClientService.Initializer
   {
    HttpClientInitializer = credential,
    ApplicationName = "Lead Sync"
    });


    }


    public async Task<List<Dictionary<string, object>>> ReadRowsAsync()
    {
        var range = $"{SheetName}!A:Z";
        var request=_service.Spreadsheets.Values.Get(_spreadsheetId,range);
        var response=await request.ExecuteAsync();

        var rows = new List<Dictionary<string, object>>();
        if (response.Values == null || response.Values.Count == 0)
            return rows;

        var headers = response.Values[0].Select(h => h?.ToString()?.Trim().ToLower() ?? "").ToList();

        for (int i = 1; i < response.Values.Count; i++)
        {
            var row = new Dictionary<string, object>();
            var values = response.Values[i];

            for (int j = 0; j < headers.Count; j++)
            {
                var value = j < values.Count ? values[j] : null;
                row[headers[j]] = value ?? "";
            }

            rows.Add(row);
        }

        return rows;

    }





    public async Task<int?> FindRowIndexByIdAsync(string sheetIdValue)
    {
        var range = $"{SheetName}!A:A";
        var request = _service.Spreadsheets.Values.Get(_spreadsheetId, range);
        var response = await request.ExecuteAsync();

        if (response.Values == null || response.Values.Count == 0)
            return null;

        // Check if first row contains "id" header
        var firstCell = response.Values[0][0]?.ToString()?.Trim().ToLower();
        if (firstCell != "id")
        {
            // Try to find ID column in first row
            var headerRange = $"{SheetName}!1:1";
            var headerRequest = _service.Spreadsheets.Values.Get(_spreadsheetId, headerRange);
            var headerResponse = await headerRequest.ExecuteAsync();
            
            if (headerResponse.Values != null && headerResponse.Values.Count > 0)
            {
                var headers = headerResponse.Values[0];
                for (int i = 0; i < headers.Count; i++)
                {
                    if (headers[i]?.ToString()?.Trim().ToLower() == "id")
                    {
                        var columnLetter = GetColumnLetter(i + 1);
                        range = $"{SheetName}!{columnLetter}:{columnLetter}";
                        request = _service.Spreadsheets.Values.Get(_spreadsheetId, range);
                        response = await request.ExecuteAsync();
                        break;
                    }
                }
            }
        }

        if (response.Values == null)
            return null;

        for (int i = 1; i < response.Values.Count; i++)
        {
            if (response.Values[i].Count > 0)
            {
                var cellValue = response.Values[i][0]?.ToString()?.Trim();
                if (cellValue == sheetIdValue.Trim())
                {
                    return i + 1; // 1-indexed
                }
            }
        }

        return null;
    }

    


    public async Task UpdateCategoryByRowIndexAsync(int rowIndex, string newCategory)
    {
        await UpdateCellByColumnNameAsync(rowIndex, "category", newCategory);
    }
    public async Task UpdateNameByRowIndexAsync(int rowIndex, string newName)
    {
        await UpdateCellByColumnNameAsync(rowIndex, "name", newName);
    }

    public async Task UpdateEmailByRowIndexAsync(int rowIndex, string newEmail)
    {
        await UpdateCellByColumnNameAsync(rowIndex, "email", newEmail);
    }

    public async Task UpdateNoteByRowIndexAsync(int rowIndex, string newNote)
    {
        await UpdateCellByColumnNameAsync(rowIndex, "note", newNote);
    }

    public async Task UpdateSourceByRowIndexAsync(int rowIndex, string newSource)
    {
        await UpdateCellByColumnNameAsync(rowIndex, "source", newSource);
    }

    private async Task UpdateCellByColumnNameAsync(int rowIndex, string columnName, string value)
    {
        var headerRange = $"{SheetName}!1:1";
        var request = _service.Spreadsheets.Values.Get(_spreadsheetId, headerRange);
        var response = await request.ExecuteAsync();
        
        if (response.Values == null || response.Values.Count == 0)
            throw new InvalidOperationException("Cannot read sheet headers");

        var headers = response.Values[0];
        int? colIndex = null;

        for (int i = 0; i < headers.Count; i++)
        {
            if (headers[i]?.ToString()?.Trim().ToLower() == columnName.ToLower())
            {
                colIndex = i + 1;
                break;
            }
        }

        if (colIndex == null)
            throw new InvalidOperationException($"Sheet has no '{columnName}' header");
        
        var columnLetter = GetColumnLetter(colIndex.Value);
        var updateRange = $"{SheetName}!{columnLetter}{rowIndex}";

        var valueRange = new ValueRange
        {
            Values = new List<IList<object>> { new List<object> { value } }
        };


        var updateRequest = _service.Spreadsheets.Values.Update(valueRange, _spreadsheetId, updateRange);
        updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await updateRequest.ExecuteAsync();





    }

    private string GetColumnLetter(int columnNumber)
    {
        string columnLetter = "";
        while (columnNumber > 0)
        {
            int modulo = (columnNumber - 1) % 26;
            columnLetter = Convert.ToChar('A' + modulo) + columnLetter;
            columnNumber = (columnNumber - modulo) / 26;
        }
        return columnLetter;
    }








}