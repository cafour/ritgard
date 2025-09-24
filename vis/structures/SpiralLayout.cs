using System.Collections.Generic;

namespace Ritgard.Structures;

public class SpiralLayout : ILayout
{
    public float Constant { get; set; } = 1.0f;

    public float Step { get; set; } = 1.0f;

    public float InitialRadius { get; set; } = 0.0f;

    public float InitialAngle { get; set; } = 0.0f;

    public void ComputeLayout(IList<ILayoutable> objects)
    {
        // var radius = InitialRadius
        // var angle = ;
        foreach (var @object in objects)
        {

        }
    }
}
