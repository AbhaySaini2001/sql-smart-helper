namespace SqlConstraintHelper.Core.Models
{
    /// <summary>
    /// Represents a node in the schema graph (a table)
    /// </summary>
    public class GraphNode
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public int RowCount { get; set; }
        public NodeType Type { get; set; } = NodeType.Standard;
        public NodeColor Color { get; set; } = NodeColor.Blue;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 120;
        public double Height { get; set; } = 60;
        public bool IsSelected { get; set; }
        public bool IsHighlighted { get; set; }
        public bool IsOrphaned { get; set; }
        public int IncomingEdges { get; set; }
        public int OutgoingEdges { get; set; }
        public List<string> PrimaryKeys { get; set; } = new();
        public List<string> ForeignKeys { get; set; } = new();
    }

    /// <summary>
    /// Represents an edge in the schema graph (a foreign key relationship)
    /// </summary>
    public class GraphEdge
    {
        public string Id { get; set; } = string.Empty;
        public string SourceNodeId { get; set; } = string.Empty;
        public string TargetNodeId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string ConstraintName { get; set; } = string.Empty;
        public string SourceColumn { get; set; } = string.Empty;
        public string TargetColumn { get; set; } = string.Empty;
        public EdgeType Type { get; set; } = EdgeType.OneToMany;
        public bool IsEnabled { get; set; } = true;
        public bool IsHighlighted { get; set; }
        public string DeleteAction { get; set; } = "NO ACTION";
        public string UpdateAction { get; set; } = "NO ACTION";
        public double Thickness { get; set; } = 2;
    }

    /// <summary>
    /// Complete schema graph with nodes and edges
    /// </summary>
    public class SchemaGraph
    {
        public List<GraphNode> Nodes { get; set; } = new();
        public List<GraphEdge> Edges { get; set; } = new();
        public DateTime GeneratedDate { get; set; } = DateTime.Now;
        public string DatabaseName { get; set; } = string.Empty;
        public GraphStatistics Statistics { get; set; } = new();
    }

    /// <summary>
    /// Graph statistics for display
    /// </summary>
    public class GraphStatistics
    {
        public int TotalTables { get; set; }
        public int TotalRelationships { get; set; }
        public int OrphanedTables { get; set; }
        public int DisabledConstraints { get; set; }
        public int CircularReferences { get; set; }
    }

    /// <summary>
    /// Graph layout options
    /// </summary>
    public class GraphLayoutOptions
    {
        public LayoutAlgorithm Algorithm { get; set; } = LayoutAlgorithm.Hierarchical;
        public double NodeSpacing { get; set; } = 50;
        public double LayerSpacing { get; set; } = 100;
        public bool GroupBySchema { get; set; } = true;
        public bool MinimizeCrossings { get; set; } = true;
    }

    /// <summary>
    /// Graph filter options
    /// </summary>
    public class GraphFilter
    {
        public List<string> IncludeSchemas { get; set; } = new();
        public List<string> ExcludeSchemas { get; set; } = new();
        public List<string> IncludeTables { get; set; } = new();
        public bool ShowOrphanedOnly { get; set; }
        public bool ShowWithIssuesOnly { get; set; }
        public int MinRowCount { get; set; }
        public int MaxRowCount { get; set; } = int.MaxValue;
    }

    public enum NodeType
    {
        Standard,      // Regular table
        Primary,       // Central/important table
        Lookup,        // Reference/lookup table
        Orphaned,      // No relationships
        Junction       // Many-to-many junction table
    }

    public enum NodeColor
    {
        Blue,
        Green,
        Orange,
        Red,
        Purple,
        Gray,
        Yellow
    }

    public enum EdgeType
    {
        OneToOne,
        OneToMany,
        ManyToMany
    }

    public enum LayoutAlgorithm
    {
        Hierarchical,  // Top-down tree layout
        Circular,      // Circular arrangement
        Force,         // Force-directed physics
        Grid,          // Grid-based layout
        Organic        // Natural clustering
    }

    /// <summary>
    /// Export options for graph visualization
    /// </summary>
    public class GraphExportOptions
    {
        public ExportFormat Format { get; set; } = ExportFormat.PNG;
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public int Quality { get; set; } = 95;
        public bool IncludeLegend { get; set; } = true;
        public bool IncludeStatistics { get; set; } = true;
        public string? OutputPath { get; set; }
    }
}