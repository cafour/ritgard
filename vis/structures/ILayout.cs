using System.Collections;
using System.Collections.Generic;

namespace Ritgard.Structures;

public interface ILayout
{
    void ComputeLayout(IList<ILayoutable> objects);
}
