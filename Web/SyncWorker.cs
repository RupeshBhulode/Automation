using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace DotNet2;

public class SyncWorker : BackgroundService
{
    private readonly ILogger<SyncWorker> _logger;
    private readonly GoogleSheetClient _sheetClient;
    private readonly TrelloClient _trelloClient;

    private readonly SyncLogic _syncLogic;
    private readonly StateManager _stateManager;
    private readonly int _pollIntervalSeconds;


    public SyncWorker(
        ILogger<SyncWorker> logger,
        GoogleSheetClient sheetClient,
        TrelloClient trelloClient,
        SyncLogic syncLogic,
        StateManager stateManager,
        IConfiguration configuration
    )
    {
        _logger = logger;
        _sheetClient = sheetClient;
        _trelloClient = trelloClient;
        _syncLogic = syncLogic;
        _stateManager = stateManager;
        _pollIntervalSeconds = configuration.GetValue<int>("PollIntervalSeconds", 60);
    }



    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncWorker starting. Poll interval: {Interval} seconds", _pollIntervalSeconds);

        await _trelloClient.EnsureListMapAsync(new List<string>{"TODO","INPROGRESS","DONE"});

        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting sync cycle at {Time}", DateTimeOffset.Now);
                var state = _stateManager.GetState();

                await _syncLogic.SyncSheetToTrelloAsync(
                     _sheetClient,
                    _trelloClient,
                    state.Mappings,
                    state,
                    async () => await _stateManager.SaveStateAsync()
                    
                );



                await _syncLogic.SyncTrelloToSheetAsync(
                    _sheetClient,
                    _trelloClient,
                    state.Mappings,
                    state,
                    async () => await _stateManager.SaveStateAsync()
                    );

                     _logger.LogInformation("Sync cycle completed at {Time}", DateTimeOffset.Now);



                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sync loop");
            }


            await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), stoppingToken);

        }
        _logger.LogInformation("SyncWorker stopped");


    }




}