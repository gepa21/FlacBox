using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// Used to signify page border.
    /// </summary>
    interface IPageble
    {
        void EndOfPage(bool last);
    }

    class NullForIPageble : IPageble
    {
        internal static readonly NullForIPageble Instance = new NullForIPageble();

        public void EndOfPage(bool last)
        {
        }
    }
}
