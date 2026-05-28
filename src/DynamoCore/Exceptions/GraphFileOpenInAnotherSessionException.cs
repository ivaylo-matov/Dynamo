using System;

namespace Dynamo.Exceptions
{
    /// <summary>
    /// Thrown when attempting to open a graph file that is already open in another
    /// active Dynamo session.
    /// </summary>
    internal class GraphFileOpenInAnotherSessionException : Exception
    {
        /// <summary>
        /// Path to the graph file that is already open elsewhere.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Creates a new instance of <see cref="GraphFileOpenInAnotherSessionException"/>.
        /// </summary>
        /// <param name="filePath">Path to the graph file.</param>
        public GraphFileOpenInAnotherSessionException(string filePath)
            : base(filePath)
        {
            FilePath = filePath;
        }
    }
}
