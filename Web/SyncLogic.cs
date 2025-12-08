using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace DotNet2;

public class SyncLogic
{
    private readonly ILogger<SyncLogic>_logger;

    private static readonly Dictionary<string,string?> SheetToTrello = new()
    {
        {"new","todo"},
        {"contacted","inprogress"},
        {"qualified","done"},
        {"lost",null}
    };

    private static readonly Dictionary<string,string> TrelloToSheet = new()
    {
        {"todo","new"},
        {"inprogress","contacted"},
        {"done","qualified"}
    };



    public SyncLogic(ILogger<SyncLogic> logger)
    {
        _logger=logger;
    }

    public async Task SyncSheetToTrelloAsync(
        GoogleSheetClient sheet,
        TrelloClient trello,
        Dictionary<string, LeadMapping> mappings,
        StateData state,
        Func<Task> saveStateCallback
    )
    {
        var rows= await sheet.ReadRowsAsync();

        foreach(var r in rows)
        {
            var sidRaw = r.GetValueOrDefault("id");
            if (sidRaw == null || string.IsNullOrWhiteSpace(sidRaw.ToString()))
                continue;

            var sid = sidRaw.ToString()?.Trim() ?? "";
            var name = r.GetValueOrDefault("name")?.ToString()?.Trim() ?? "";
            var email = r.GetValueOrDefault("email")?.ToString()?.Trim() ?? "";
            var note = r.GetValueOrDefault("note")?.ToString()?.Trim() ?? "";
            var source = r.GetValueOrDefault("source")?.ToString()?.Trim() ?? "";
            var sheetCategoryRaw = r.GetValueOrDefault("category")?.ToString()?.Trim().ToLower() ?? "";


            var mappedListForSheet = SheetToTrello.GetValueOrDefault(sheetCategoryRaw);
            if (!mappings.ContainsKey(sid))
            {
                if (mappedListForSheet != null)
                {
                    var cardName=$"{name} (LeadID: {sid})";
                    var descFields = new Dictionary<string, string>
                    {
                        { "Email", email },
                        { "Note", note },
                        { "Source", source }
                    };

                    var desc = trello.RenderFieldsToDesc(descFields);
                    var cardId = await trello.CreateCardAsync(mappedListForSheet, cardName, desc);
                    mappings[sid] = new LeadMapping
                        {
                            CardId = cardId,
                            Category = sheetCategoryRaw,
                            Name = name,
                            Email = email,
                            Note = note,
                            Source = source
                        };



                    await saveStateCallback();
                    _logger.LogInformation("Created card for sheet id={Sid} -> trello card id={CardId} (list={List})",
                            sid, cardId, mappedListForSheet);







                }
            }
            else
            {
                var mapped = mappings[sid];
                var mappedCategory = mapped.Category?.Trim().ToLower() ?? "";
                var cardId = mapped.CardId;

                if(sheetCategoryRaw != mappedCategory)
                {
                    if(mappedListForSheet==null)
                    {
                        if (!string.IsNullOrEmpty(cardId))
                        {
                            try
                            {
                                await trello.ArchiveCardAsync(cardId);
                                _logger.LogInformation("Archived Trello card {CardId} because sheet id {Sid} changed to LOST",
                                    cardId, sid);
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e, "Failed to archive Trello card {CardId} for sid {Sid}", cardId, sid);
                            }
                        }
                        mappings.Remove(sid);
                        await saveStateCallback();

                    }
                    else
                    {
                        try
                        {
                            await trello.GetListsByNameAsync();
                            if (string.IsNullOrEmpty(cardId))
                            {
                                var cardName = $"{name} (LeadID: {sid})";
                                var desc = trello.RenderFieldsToDesc(new Dictionary<string, string>
                                {
                                    { "Email", email },
                                    { "Note", note },
                                    { "Source", source }
                                });
                                cardId = await trello.CreateCardAsync(mappedListForSheet, cardName, desc);
                                mappings[sid] = new LeadMapping
                                {
                                    CardId = cardId,
                                    Category = sheetCategoryRaw,
                                    Name = name,
                                    Email = email,
                                    Note = note,
                                    Source = source
                                };
                                await saveStateCallback();
                                _logger.LogInformation("Re-created Trello card for sheet id={Sid} -> {CardId}", sid, cardId);
                            }
                            else
                            {
                                await trello.MoveCardAsync(cardId, mappedListForSheet);
                                await trello.UpdateCardFieldsAsync(cardId, new Dictionary<string, string>
                                {
                                    { "email", email },
                                    { "note", note },
                                    { "source", source }
                                });
                                mapped.Category = sheetCategoryRaw;
                                mapped.Name = name;
                                mapped.Email = email;
                                mapped.Note = note;
                                mapped.Source = source;
                                await saveStateCallback();
                                _logger.LogInformation(
                                    "Moved Trello card {CardId} -> list {List} (sheet id {Sid} changed category)",
                                    cardId, mappedListForSheet, sid);
                            }
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Failed handling category change for sid {Sid}", sid);
                        }
                    }
                }


                // Name change
                var mappedName = mapped.Name?.Trim() ?? "";
                if (name != mappedName && !string.IsNullOrEmpty(cardId))
                {
                    try
                    {
                        await trello.UpdateCardNameAsync(cardId, $"{name} (LeadID: {sid})");
                        mapped.Name = name;
                        await saveStateCallback();
                        _logger.LogInformation("Updated Trello card {CardId} title -> {Name} (due to sheet change)", cardId, name);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to update Trello card title for {CardId}", cardId);
                    }
                }




                if (!string.IsNullOrEmpty(cardId))
                {
                    var toUpdate = new Dictionary<string, string>();
                    if (email != (mapped.Email?.Trim() ?? ""))
                        toUpdate["email"] = email;
                    if (note != (mapped.Note?.Trim() ?? ""))
                        toUpdate["note"] = note;
                    if (source != (mapped.Source?.Trim() ?? ""))
                        toUpdate["source"] = source;

                    if (toUpdate.Any())
                    {
                        try
                        {
                            await trello.UpdateCardFieldsAsync(cardId, toUpdate);
                            if (toUpdate.ContainsKey("email")) mapped.Email = email;
                            if (toUpdate.ContainsKey("note")) mapped.Note = note;
                            if (toUpdate.ContainsKey("source")) mapped.Source = source;
                            await saveStateCallback();
                            _logger.LogInformation("Updated Trello card {CardId} desc fields -> {Fields}",
                                cardId, string.Join(", ", toUpdate.Keys));
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Failed to update Trello card desc for {CardId}", cardId);
                        }
                    }
                }
            }

        }
    }

 

    public async Task SyncTrelloToSheetAsync(
        GoogleSheetClient sheet,
        TrelloClient trello,
        Dictionary<string, LeadMapping> mappings,
        StateData state,
        Func<Task> saveStateCallback
    )
    {

        try
        {
            var cards = await trello.GetCardsOnBoardAsync();
            var lists = await trello.GetListsByNameAsync();
            var listIdToName = lists.ToDictionary(kvp=>kvp.Value , kvp=>kvp.Key);
            var cardsById = cards.ToDictionary(c=> c["id"]?.ToString()?? "");

            foreach (var kvp in mappings.ToList())
            {
                var sid = kvp.Key;
                var mapped = kvp.Value;
                var cardId = mapped.CardId;

                if (string.IsNullOrEmpty(cardId))
                    continue;

                if (!cardsById.ContainsKey(cardId))
                {
                    _logger.LogInformation("Card {CardId} for sheet id {Sid} missing/archived; setting sheet category to LOST",
                        cardId, sid);
                    var rowIndex = await sheet.FindRowIndexByIdAsync(sid);
                    if (rowIndex.HasValue)
                    {
                        try
                        {
                            await sheet.UpdateCategoryByRowIndexAsync(rowIndex.Value, "LOST");
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Failed setting sheet category to LOST for sid {Sid}", sid);
                        }
                    }

                    mappings.Remove(sid);
                    await saveStateCallback();
                    continue;



                }


                 var cardInfo = cardsById[cardId];
                 var currentListId = cardInfo["idList"]?.ToString();
                 var currentListName = listIdToName.GetValueOrDefault(currentListId ?? "")?.ToLower().Trim();
                var mappedCategory = mapped.Category?.Trim().ToLower() ?? "";
                var mappedSheetCat = TrelloToSheet.GetValueOrDefault(currentListName ?? "");

                if (!string.IsNullOrEmpty(mappedSheetCat) && mappedSheetCat != mappedCategory)
                {
                    var rowIndex = await sheet.FindRowIndexByIdAsync(sid);
                    if (rowIndex.HasValue)
                    {
                        try
                        {
                            await sheet.UpdateCategoryByRowIndexAsync(rowIndex.Value, mappedSheetCat);
                            mapped.Category = mappedSheetCat;
                            await saveStateCallback();
                            _logger.LogInformation("Updated sheet row {Sid} category -> {Category} because Trello card {CardId} moved",
                                sid, mappedSheetCat, cardId);                             

                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Failed updating sheet category for sid {Sid}", sid);
                        }
                    }
                }


                var cardDesc = cardInfo["desc"]?.ToString() ?? "";
                var parsed = trello.ParseDescToFields(cardDesc);
                var trelloCardEmail=parsed.GetValueOrDefault("email")?.Trim() ?? "";
                var trelloCardNote = parsed.GetValueOrDefault("note")?.Trim() ?? "";
                var trelloCardSource = parsed.GetValueOrDefault("source")?.Trim() ?? "";

                var trelloCardName = cardInfo["name"]?.ToString()?.Trim() ?? "";
                var trelloCardNameClean = trelloCardName.Split('(')[0].Trim();
                trelloCardName = trelloCardNameClean;
                var mappedName = mapped.Name?.Trim() ?? "";

                if (!string.IsNullOrEmpty(trelloCardName) && trelloCardName != mappedName)
                {
                    var rowIndex = await sheet.FindRowIndexByIdAsync(sid);
                    if (rowIndex.HasValue)
                    {
                        try
                        {
                            await sheet.UpdateNameByRowIndexAsync(rowIndex.Value, trelloCardName);
                            mapped.Name = trelloCardName;
                            await saveStateCallback();
                            _logger.LogInformation("Updated sheet row {Sid} name -> {Name} because Trello card {CardId} title changed",
                                sid, trelloCardName, cardId);
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Failed updating sheet name for sid {Sid}", sid);
                        }
                    }
                }

                var mappedEmail = mapped.Email?.Trim() ?? "";
                if (!string.IsNullOrEmpty(trelloCardEmail) && trelloCardEmail != mappedEmail)
                {
                    var rowIndex = await sheet.FindRowIndexByIdAsync(sid);
                    if (rowIndex.HasValue)
                    {
                        try
                        {
                            await sheet.UpdateEmailByRowIndexAsync(rowIndex.Value, trelloCardEmail);
                            mapped.Email = trelloCardEmail;
                            await saveStateCallback();
                            _logger.LogInformation("Updated sheet row {Sid} email -> {Email} because Trello card {CardId} desc changed",
                                sid, trelloCardEmail, cardId);
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Failed updating sheet email for sid {Sid}", sid);
                        }
                    }
                }

                // Update note
                var mappedNote = mapped.Note?.Trim() ?? "";
                if (!string.IsNullOrEmpty(trelloCardNote) && trelloCardNote != mappedNote)
                {
                    var rowIndex = await sheet.FindRowIndexByIdAsync(sid);
                    if (rowIndex.HasValue)
                    {
                        try
                        {
                            await sheet.UpdateNoteByRowIndexAsync(rowIndex.Value, trelloCardNote);
                            mapped.Note = trelloCardNote;
                            await saveStateCallback();
                            _logger.LogInformation("Updated sheet row {Sid} note -> {Note} because Trello card {CardId} desc changed",
                                sid, trelloCardNote, cardId);
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Failed updating sheet note for sid {Sid}", sid);
                        }
                    }
                }


                var mappedSource = mapped.Source?.Trim() ?? "";
                if (!string.IsNullOrEmpty(trelloCardSource) && trelloCardSource != mappedSource)
                {
                    var rowIndex = await sheet.FindRowIndexByIdAsync(sid);
                    if (rowIndex.HasValue)
                    {
                        try
                        {
                            await sheet.UpdateSourceByRowIndexAsync(rowIndex.Value, trelloCardSource);
                            mapped.Source = trelloCardSource;
                            await saveStateCallback();
                            _logger.LogInformation("Updated sheet row {Sid} source -> {Source} because Trello card {CardId} desc changed",
                                sid, trelloCardSource, cardId);
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Failed updating sheet source for sid {Sid}", sid);
                        }
                    }
                }

                







            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while pulling Trello board/cards");
        }
    }






}