using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace DotNet2;

public class TrelloClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _token;
    private readonly string _boardId;
    private readonly Dictionary<string,string>_lists=new();

    private const string BaseUrl = "https://api.trello.com/1";
    
    public TrelloClient(HttpClient httpClient , IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Trello:ApiKey"];
        _token = configuration["Trello:Token"];
        _boardId = configuration["Trello:BoardId"];

        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_token) || string.IsNullOrEmpty(_boardId))
        {
            throw new InvalidOperationException("Trello API key, token, and board ID must be configured");
        }
    }


    private string AddAuth(string url)
    {
        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}key={_apiKey}&token={_token}";
    }


    private async Task<T> GetAsync<T>(string path, Dictionary<string,string>?queryParams=null)
    {
        var url=$"{BaseUrl}{path}";
        if (queryParams != null && queryParams.Any())
        {
            var query = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            url += $"?{query}";
        }


        url=AddAuth(url);

        var response=await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        return JsonConvert.DeserializeObject<T>(content);

    }



    private async Task<T> PostAsync<T>(string path, Dictionary<string,string>data)
    {
        var url = AddAuth($"{BaseUrl}{path}");
        var content = new FormUrlEncodedContent(data);
        var response =await _httpClient.PostAsync(url,content);
        var responseContent = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<T>(responseContent);
    }

     private async Task<T> PutAsync<T>(string path, Dictionary<string, string> data)
    {
        var url = AddAuth($"{BaseUrl}{path}");
        var content = new FormUrlEncodedContent(data);
        var response = await _httpClient.PutAsync(url, content);
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<T>(responseContent);
    }
    

    public async Task<Dictionary<string,string>>GetListsByNameAsync()
    {
        var lists=await GetAsync<List<JObject>>($"/boards/{_boardId}/lists");
        _lists.Clear();

        foreach (var list in lists)
        {
            var name = list["name"]?.ToString()?.Trim().ToLower() ?? "";
            var id = list["id"]?.ToString() ?? "";
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
            {
                _lists[name] = id;
            }
        }

        return _lists;
    }



    public async Task<Dictionary<string,string>> EnsureListMapAsync(List<string> requiredNames)
    {
        await GetListsByNameAsync();
        foreach (var name in requiredNames)
        {
            var lname = name.ToLower().Trim();
            if (!_lists.ContainsKey(lname))
            {
                var data = new Dictionary<string, string>
                {
                    { "name", name },
                    { "idBoard", _boardId },
                    { "pos", "bottom" }
                };


                var created = await PostAsync<JObject>("/lists", data);
                var id = created["id"]?.ToString();
                if (!string.IsNullOrEmpty(id))
                {
                    _lists[lname] = id;
                }
            }
        }
        return _lists;

    }


    public async Task<string> CreateCardAsync(string listName, string cardName, string desc = "")
    {
        var lname = listName.ToLower().Trim();
        
        if (_lists.Count == 0)
        {
            await GetListsByNameAsync();
        }

        if (!_lists.ContainsKey(lname))
        {
            throw new InvalidOperationException($"Trello list '{listName}' not found on board {_boardId}");
        }

        var listId = _lists[lname];
        var data = new Dictionary<string, string>
        {
            { "name", cardName },
            { "idList", listId },
            { "desc", desc }
        };

        var card = await PostAsync<JObject>("/cards", data);
        return card["id"]?.ToString() ?? "";
    }

    public async Task<JObject> GetCardAsync(string cardId)
    {
        return await GetAsync<JObject>($"/cards/{cardId}");
    }

    public async Task<JObject> UpdateCardNameAsync(string cardId, string newName)
    {
        var data = new Dictionary<string, string> { { "name", newName } };
        return await PutAsync<JObject>($"/cards/{cardId}", data);
    }

     public async Task<JObject> UpdateCardFieldsAsync(string cardId, Dictionary<string, string> newFields)
    {
        var card = await GetCardAsync(cardId);
        var currentDesc = card["desc"]?.ToString() ?? "";
        var parsed = ParseDescToFields(currentDesc);

        foreach (var kvp in newFields)
        {
            if (kvp.Value != null)
            {
                parsed[kvp.Key.Trim().ToLower()] = kvp.Value.Trim();
            }
        }
        
        var canon = new Dictionary<string, string>();
        if (parsed.ContainsKey("email")) canon["Email"] = parsed["email"];
        if (parsed.ContainsKey("note")) canon["Note"] = parsed["note"];
        if (parsed.ContainsKey("source")) canon["Source"] = parsed["source"];

        var newDesc = RenderFieldsToDesc(canon);
        var data = new Dictionary<string, string> { { "desc", newDesc } };
        return await PutAsync<JObject>($"/cards/{cardId}", data);
    }


    public async Task<JObject> MoveCardAsync(string cardId, string destListName)
    {
        if (_lists.Count == 0)
        {
            await GetListsByNameAsync();
        }

        var lname = destListName.ToLower().Trim();
        if (!_lists.ContainsKey(lname))
        {
            throw new InvalidOperationException($"Destination list '{destListName}' not found");
        }

        var destListId = _lists[lname];
        var data = new Dictionary<string, string> { { "idList", destListId } };
        return await PutAsync<JObject>($"/cards/{cardId}", data);
    }

    public async Task<List<JObject>> GetCardsOnBoardAsync()
    {
        return await GetAsync<List<JObject>>($"/boards/{_boardId}/cards");
    }

    public async Task<JObject> ArchiveCardAsync(string cardId)
    {
        var data = new Dictionary<string, string> { { "closed", "true" } };
        return await PutAsync<JObject>($"/cards/{cardId}", data);
    }


    public string RenderFieldsToDesc(Dictionary<string, string> fields)
    {
        var parts = new List<string>();

        if (fields.Any(k => k.Key.ToLower() == "email"))
        {
            var email = fields.FirstOrDefault(k => k.Key.ToLower() == "email").Value;
            if (!string.IsNullOrEmpty(email))
                parts.Add($"Email: {email}");
        }

        if (fields.Any(k => k.Key.ToLower() == "note"))
        {
            var note = fields.FirstOrDefault(k => k.Key.ToLower() == "note").Value;
            if (!string.IsNullOrEmpty(note))
                parts.Add($"Note: {note}");
        }

        if (fields.Any(k => k.Key.ToLower() == "source"))
        {
            var source = fields.FirstOrDefault(k => k.Key.ToLower() == "source").Value;
            if (!string.IsNullOrEmpty(source))
                parts.Add($"Source: {source}");
        }

        return string.Join("\n", parts);
    }


    public Dictionary<string, string> ParseDescToFields(string desc)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(desc))
            return result;


        foreach (var line in desc.Split('\n'))
        {
            if (!line.Contains(':'))
                continue;

            var parts = line.Split(':', 2);
            var key = parts[0].Trim().ToLower();
            var value = parts[1].Trim();

            if (string.IsNullOrEmpty(value))
                continue;

            if (key == "email")
            {
                result[key] = ExtractEmail(value);
            }
            else if (key == "note" || key == "source")
            {
                result[key] = value;
            }
        }

        return result;    

    }



    public string ExtractEmail(string text)
    {
        var match = Regex.Match(text, @"([A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,})");
        return match.Success ? match.Groups[1].Value : text;
    }
  













}