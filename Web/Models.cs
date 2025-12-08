namespace DotNet2;
public class LeadMapping
{
    public string? CardId { get; set; }
    public string? Category { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Note { get; set; }
    public string? Source { get; set; }
}

public class StateData
{
    public Dictionary<string,LeadMapping> Mappings {get;set;} = new();
}