using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// Base FlacBox exception class.
    /// </summary>
    public class FlacException : Exception
    {
        public FlacException(string message)
            : base(message)
        {
        }
    }
}
