using System;
using System.Collections.Generic;
using UnityEngine;

namespace NodeImageEditor
{
    [Serializable]
    public class NodeGraph
    {
        [SerializeField] private List<BaseNode> nodes = new List<BaseNode>();
        [SerializeField] private List<NodeConnection> connections = new List<NodeConnection>();
        
        private Dictionary<string, BaseNode> nodeMap = new Dictionary<string, BaseNode>();
        
        public void AddNode(BaseNode node)
        {
            nodes.Add(node);
            nodeMap[node.NodeID] = node;
        }
        
        public void RemoveNode(string nodeID)
        {
            BaseNode nodeToRemove = GetNodeByID(nodeID);
            if (nodeToRemove != null)
            {
                nodes.Remove(nodeToRemove);
                nodeMap.Remove(nodeID);
                
                // Remove all connections associated with this node
                connections.RemoveAll(c => c.FromNodeID == nodeID || c.ToNodeID == nodeID);
            }
        }
        
        public void ConnectNodes(string fromNodeID, int fromPortIndex, string toNodeID, int toPortIndex)
        {
            BaseNode fromNode = GetNodeByID(fromNodeID);
            BaseNode toNode = GetNodeByID(toNodeID);
            
            if (fromNode != null && toNode != null)
            {
                NodeConnection newConnection = new NodeConnection(fromNodeID, fromPortIndex, toNodeID, toPortIndex);
                connections.Add(newConnection);
                
                fromNode.AddOutputConnection(toNode, fromPortIndex, toPortIndex);
                toNode.AddInputConnection(fromNode, fromPortIndex, toPortIndex);
            }
        }
        
        public void DisconnectNodes(string fromNodeID, int fromPortIndex, string toNodeID, int toPortIndex)
        {
            NodeConnection connectionToRemove = connections.Find(c => 
                c.FromNodeID == fromNodeID && 
                c.FromPortIndex == fromPortIndex && 
                c.ToNodeID == toNodeID && 
                c.ToPortIndex == toPortIndex);
                
            if (connectionToRemove != null)
            {
                connections.Remove(connectionToRemove);
                
                BaseNode fromNode = GetNodeByID(fromNodeID);
                BaseNode toNode = GetNodeByID(toNodeID);
                
                if (fromNode != null) fromNode.RemoveConnection(connectionToRemove);
                if (toNode != null) toNode.RemoveConnection(connectionToRemove);
            }
        }
        
        public BaseNode GetNodeByID(string nodeID)
        {
            if (nodeMap.TryGetValue(nodeID, out BaseNode node))
            {
                return node;
            }
            
            // If nodeMap is not initialized yet, find it manually
            foreach (var n in nodes)
            {
                if (n.NodeID == nodeID) return n;
            }
            
            return null;
        }
        
        public List<BaseNode> GetAllNodes()
        {
            return nodes;
        }
        
        public List<BaseNode> GetNodes()
        {
            return nodes;
        }
        
        public List<NodeConnection> GetAllConnections()
        {
            return connections;
        }
        
        public void ProcessGraph(string outputNodeID)
        {
            // Make sure the nodeMap is up to date
            if (nodeMap.Count != nodes.Count)
            {
                nodeMap.Clear();
                foreach (var node in nodes)
                {
                    nodeMap[node.NodeID] = node;
                }
            }
            
            BaseNode outputNode = GetNodeByID(outputNodeID);
            if (outputNode != null)
            {
                outputNode.ProcessTexture(nodeMap);
            }
        }
    }
} 