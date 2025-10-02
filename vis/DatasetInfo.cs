using Godot;

namespace Ritgard;

[GlobalClass]
public partial class DatasetInfo : Resource
{
    [Export]
    public string Name { get; set; }
    
    [Export]
    public string DataFilePath { get; set; }

    [Export]
    public string PositionsFilePath { get; set; }
}
