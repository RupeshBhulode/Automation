using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace DotNet2;

public class StateManager
{
    private readonly string _dataFilePath;
    private StateData _state;
    private readonly SemaphoreSlim _lock = new(1,1);


    public StateManager(IConfiguration configuration)
    {
        _dataFilePath=configuration["DataJsonPath"]?? "data.json";
        _state=LoadState();
    }

    public StateData GetState()
    {
        return _state;
    }

    public StateData LoadState()
    {
        try
        {
            if (File.Exists(_dataFilePath))
            {
                var json=File.ReadAllText(_dataFilePath);
                return JsonConvert.DeserializeObject<StateData>(json) ?? new StateData();
            }
        }
        catch(Exception)
        {
            
        }


        return new StateData();
    }


    public async Task SaveStateAsync()
    {
        await _lock.WaitAsync();
        try
        {
             var json = JsonConvert.SerializeObject(_state, Formatting.Indented);
             await File.WriteAllTextAsync(_dataFilePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }



}