namespace DotnetVisualizer.Core;

/// <summary>
/// Describes how to render self-references in the graph.
/// </summary>
public enum SelfReferenceMode
{
    /// <summary>
    /// Do not draw the edge.
    /// </summary>
    Hide,

    /// <summary>
    /// Draw a standard edge.
    /// </summary>
    Show,

    /// <summary>
    /// Draw a labelled dotted edge.
    /// </summary>
    Highlight
}