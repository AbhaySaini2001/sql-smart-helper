using SqlConstraintHelper.Core.Models;
using System;

namespace SqlConstraintHelper.Core.Services
{
    public interface IGraphService
    {
        Task<SchemaGraph> BuildGraphAsync(List<TableInfo> tables, List<ForeignKeyInfo> foreignKeys);
        Task<SchemaGraph> ApplyLayoutAsync(SchemaGraph graph, GraphLayoutOptions options);
        Task<SchemaGraph> ApplyFilterAsync(SchemaGraph graph, GraphFilter filter);
        List<GraphNode> FindRelatedNodes(SchemaGraph graph, string nodeId, int depth = 1);
        List<string> DetectCircularReferences(SchemaGraph graph);
    }

    public class GraphService : IGraphService
    {
        public async Task<SchemaGraph> BuildGraphAsync(List<TableInfo> tables, List<ForeignKeyInfo> foreignKeys)
        {
            await Task.CompletedTask;

            var graph = new SchemaGraph
            {
                DatabaseName = tables.FirstOrDefault()?.TableName ?? "Unknown"
            };

            // Build nodes from tables
            foreach (var table in tables)
            {
                var node = new GraphNode
                {
                    Id = $"{table.SchemaName}.{table.TableName}",
                    Label = table.TableName,
                    SchemaName = table.SchemaName,
                    TableName = table.TableName,
                    RowCount = Convert.ToInt32(table.RowCounts),
                    PrimaryKeys = table.Columns.Where(c => c.IsPrimaryKey).Select(c => c.ColumnName).ToList(),
                    ForeignKeys = table.Columns.Where(c => c.IsForeignKey).Select(c => c.ColumnName).ToList()
                };

                // Determine node type
                node.Type = DetermineNodeType(table, foreignKeys);
                node.Color = GetColorForNodeType(node.Type);

                graph.Nodes.Add(node);
            }

            // Build edges from foreign keys
            foreach (var fk in foreignKeys)
            {
                var edge = new GraphEdge
                {
                    Id = fk.ConstraintName,
                    SourceNodeId = $"{fk.TableSchema}.{fk.TableName}",
                    TargetNodeId = $"{fk.ReferencedSchema}.{fk.ReferencedTable}",
                    Label = fk.ColumnName,
                    ConstraintName = fk.ConstraintName,
                    SourceColumn = fk.ColumnName,
                    TargetColumn = fk.ReferencedColumn,
                    IsEnabled = fk.IsEnabled,
                    DeleteAction = fk.DeleteAction,
                    UpdateAction = fk.UpdateAction,
                    Type = EdgeType.OneToMany
                };

                graph.Edges.Add(edge);
            }

            // Update edge counts
            foreach (var node in graph.Nodes)
            {
                node.OutgoingEdges = graph.Edges.Count(e => e.SourceNodeId == node.Id);
                node.IncomingEdges = graph.Edges.Count(e => e.TargetNodeId == node.Id);
                node.IsOrphaned = node.OutgoingEdges == 0 && node.IncomingEdges == 0;
            }

            // Calculate statistics
            graph.Statistics = new GraphStatistics
            {
                TotalTables = graph.Nodes.Count,
                TotalRelationships = graph.Edges.Count,
                OrphanedTables = graph.Nodes.Count(n => n.IsOrphaned),
                DisabledConstraints = graph.Edges.Count(e => !e.IsEnabled),
                CircularReferences = DetectCircularReferences(graph).Count
            };

            return graph;
        }

        public async Task<SchemaGraph> ApplyLayoutAsync(SchemaGraph graph, GraphLayoutOptions options)
        {
            await Task.CompletedTask;

            switch (options.Algorithm)
            {
                case LayoutAlgorithm.Hierarchical:
                    ApplyHierarchicalLayout(graph, options);
                    break;
                case LayoutAlgorithm.Circular:
                    ApplyCircularLayout(graph, options);
                    break;
                case LayoutAlgorithm.Force:
                    ApplyForceDirectedLayout(graph, options);
                    break;
                case LayoutAlgorithm.Grid:
                    ApplyGridLayout(graph, options);
                    break;
                default:
                    ApplyHierarchicalLayout(graph, options);
                    break;
            }

            return graph;
        }

        private void ApplyHierarchicalLayout(SchemaGraph graph, GraphLayoutOptions options)
        {
            // Find root nodes (nodes with no incoming edges)
            var rootNodes = graph.Nodes.Where(n => n.IncomingEdges == 0).ToList();
            if (!rootNodes.Any())
            {
                rootNodes = graph.Nodes.Take(1).ToList(); // Fallback to first node
            }

            var layers = new List<List<GraphNode>>();
            var visited = new HashSet<string>();
            var currentLayer = new List<GraphNode>(rootNodes);

            // Build layers
            while (currentLayer.Any())
            {
                layers.Add(new List<GraphNode>(currentLayer));
                var nextLayer = new List<GraphNode>();

                foreach (var node in currentLayer)
                {
                    visited.Add(node.Id);
                    var children = graph.Edges
                        .Where(e => e.SourceNodeId == node.Id)
                        .Select(e => graph.Nodes.FirstOrDefault(n => n.Id == e.TargetNodeId))
                        .Where(n => n != null && !visited.Contains(n!.Id))
                        .Cast<GraphNode>()
                        .Distinct();

                    nextLayer.AddRange(children);
                }

                currentLayer = nextLayer.Distinct().ToList();
            }

            // Add unvisited nodes (orphaned or circular)
            var unvisited = graph.Nodes.Where(n => !visited.Contains(n.Id)).ToList();
            if (unvisited.Any())
            {
                layers.Add(unvisited);
            }

            // Position nodes
            double currentY = 50;
            foreach (var layer in layers)
            {
                double totalWidth = layer.Count * (options.NodeSpacing + 120);
                double currentX = -totalWidth / 2 + 400;

                foreach (var node in layer)
                {
                    node.X = currentX;
                    node.Y = currentY;
                    currentX += options.NodeSpacing + 120;
                }

                currentY += options.LayerSpacing;
            }
        }

        private void ApplyCircularLayout(SchemaGraph graph, GraphLayoutOptions options)
        {
            int nodeCount = graph.Nodes.Count;
            double radius = Math.Max(200, nodeCount * 30);
            double centerX = 400;
            double centerY = 400;
            double angleStep = 2 * Math.PI / nodeCount;

            for (int i = 0; i < nodeCount; i++)
            {
                double angle = i * angleStep;
                graph.Nodes[i].X = centerX + radius * Math.Cos(angle);
                graph.Nodes[i].Y = centerY + radius * Math.Sin(angle);
            }
        }

        private void ApplyForceDirectedLayout(SchemaGraph graph, GraphLayoutOptions options)
        {
            // Simple force-directed algorithm
            var random = new Random(42);

            // Initial random positions
            foreach (var node in graph.Nodes)
            {
                node.X = random.Next(100, 700);
                node.Y = random.Next(100, 700);
            }

            // Simulate physics for a few iterations
            int iterations = 100;
            double repulsionForce = 50000;
            double attractionForce = 0.01;
            double damping = 0.9;

            for (int iter = 0; iter < iterations; iter++)
            {
                // Calculate repulsion between all nodes
                foreach (var node1 in graph.Nodes)
                {
                    double fx = 0, fy = 0;

                    foreach (var node2 in graph.Nodes)
                    {
                        if (node1.Id == node2.Id) continue;

                        double dx = node1.X - node2.X;
                        double dy = node1.Y - node2.Y;
                        double distance = Math.Sqrt(dx * dx + dy * dy) + 0.01;

                        double force = repulsionForce / (distance * distance);
                        fx += (dx / distance) * force;
                        fy += (dy / distance) * force;
                    }

                    // Apply attraction along edges
                    foreach (var edge in graph.Edges.Where(e => e.SourceNodeId == node1.Id || e.TargetNodeId == node1.Id))
                    {
                        var otherNodeId = edge.SourceNodeId == node1.Id ? edge.TargetNodeId : edge.SourceNodeId;
                        var otherNode = graph.Nodes.FirstOrDefault(n => n.Id == otherNodeId);
                        if (otherNode == null) continue;

                        double dx = otherNode.X - node1.X;
                        double dy = otherNode.Y - node1.Y;
                        double distance = Math.Sqrt(dx * dx + dy * dy) + 0.01;

                        fx += dx * attractionForce;
                        fy += dy * attractionForce;
                    }

                    node1.X += fx * damping;
                    node1.Y += fy * damping;
                }
            }
        }

        private void ApplyGridLayout(SchemaGraph graph, GraphLayoutOptions options)
        {
            int cols = (int)Math.Ceiling(Math.Sqrt(graph.Nodes.Count));
            double cellWidth = 150;
            double cellHeight = 100;

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                int row = i / cols;
                int col = i % cols;
                graph.Nodes[i].X = 50 + col * cellWidth;
                graph.Nodes[i].Y = 50 + row * cellHeight;
            }
        }

        public async Task<SchemaGraph> ApplyFilterAsync(SchemaGraph graph, GraphFilter filter)
        {
            await Task.CompletedTask;

            var filteredGraph = new SchemaGraph
            {
                DatabaseName = graph.DatabaseName,
                GeneratedDate = graph.GeneratedDate
            };

            // Filter nodes
            var filteredNodes = graph.Nodes.AsEnumerable();

            if (filter.IncludeSchemas.Any())
            {
                filteredNodes = filteredNodes.Where(n => filter.IncludeSchemas.Contains(n.SchemaName));
            }

            if (filter.ExcludeSchemas.Any())
            {
                filteredNodes = filteredNodes.Where(n => !filter.ExcludeSchemas.Contains(n.SchemaName));
            }

            if (filter.IncludeTables.Any())
            {
                filteredNodes = filteredNodes.Where(n => filter.IncludeTables.Contains(n.TableName));
            }

            if (filter.ShowOrphanedOnly)
            {
                filteredNodes = filteredNodes.Where(n => n.IsOrphaned);
            }

            filteredNodes = filteredNodes.Where(n => n.RowCount >= filter.MinRowCount && n.RowCount <= filter.MaxRowCount);

            filteredGraph.Nodes = filteredNodes.ToList();
            var nodeIds = filteredGraph.Nodes.Select(n => n.Id).ToHashSet();

            // Filter edges (only include edges where both nodes are in filtered set)
            filteredGraph.Edges = graph.Edges
                .Where(e => nodeIds.Contains(e.SourceNodeId) && nodeIds.Contains(e.TargetNodeId))
                .ToList();

            return filteredGraph;
        }

        public List<GraphNode> FindRelatedNodes(SchemaGraph graph, string nodeId, int depth = 1)
        {
            var related = new HashSet<string> { nodeId };
            var current = new HashSet<string> { nodeId };

            for (int i = 0; i < depth; i++)
            {
                var next = new HashSet<string>();

                foreach (var id in current)
                {
                    var outgoing = graph.Edges.Where(e => e.SourceNodeId == id).Select(e => e.TargetNodeId);
                    var incoming = graph.Edges.Where(e => e.TargetNodeId == id).Select(e => e.SourceNodeId);

                    foreach (var relatedId in outgoing.Concat(incoming))
                    {
                        if (related.Add(relatedId))
                        {
                            next.Add(relatedId);
                        }
                    }
                }

                current = next;
            }

            return graph.Nodes.Where(n => related.Contains(n.Id)).ToList();
        }

        public List<string> DetectCircularReferences(SchemaGraph graph)
        {
            var cycles = new List<string>();
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();

            foreach (var node in graph.Nodes)
            {
                if (!visited.Contains(node.Id))
                {
                    DetectCyclesUtil(graph, node.Id, visited, recursionStack, new List<string>(), cycles);
                }
            }

            return cycles;
        }

        private bool DetectCyclesUtil(SchemaGraph graph, string nodeId, HashSet<string> visited,
            HashSet<string> recursionStack, List<string> path, List<string> cycles)
        {
            visited.Add(nodeId);
            recursionStack.Add(nodeId);
            path.Add(nodeId);

            var neighbors = graph.Edges.Where(e => e.SourceNodeId == nodeId).Select(e => e.TargetNodeId);

            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    if (DetectCyclesUtil(graph, neighbor, visited, recursionStack, path, cycles))
                    {
                        return true;
                    }
                }
                else if (recursionStack.Contains(neighbor))
                {
                    var cycleStart = path.IndexOf(neighbor);
                    var cycle = string.Join(" -> ", path.Skip(cycleStart).Append(neighbor));
                    cycles.Add(cycle);
                }
            }

            path.RemoveAt(path.Count - 1);
            recursionStack.Remove(nodeId);
            return false;
        }

        private NodeType DetermineNodeType(TableInfo table, List<ForeignKeyInfo> allForeignKeys)
        {
            var incomingFKs = allForeignKeys.Count(fk =>
                fk.ReferencedSchema == table.SchemaName && fk.ReferencedTable == table.TableName);
            var outgoingFKs = allForeignKeys.Count(fk =>
                fk.TableSchema == table.SchemaName && fk.TableName == table.TableName);

            if (incomingFKs == 0 && outgoingFKs == 0)
                return NodeType.Orphaned;

            if (outgoingFKs >= 2 && incomingFKs == 0)
                return NodeType.Junction;

            if (incomingFKs > 3)
                return NodeType.Primary;

            if (Convert.ToInt32(table.RowCounts) < 100 && outgoingFKs == 0)
                return NodeType.Lookup;

            return NodeType.Standard;
        }

        private NodeColor GetColorForNodeType(NodeType type)
        {
            return type switch
            {
                NodeType.Primary => NodeColor.Blue,
                NodeType.Lookup => NodeColor.Green,
                NodeType.Junction => NodeColor.Purple,
                NodeType.Orphaned => NodeColor.Gray,
                _ => NodeColor.Blue
            };
        }
    }
}