using System;
using System.Collections.Generic;
using UnityEngine;

namespace NodeImageEditor
{
    [Serializable]
    public abstract class BaseNode
    {
        public string NodeID;
        public string Name;
        public Rect WindowRect;
        
        [SerializeField] protected List<NodeConnection> inputConnections = new List<NodeConnection>();
        [SerializeField] protected List<NodeConnection> outputConnections = new List<NodeConnection>();
        
        // Cached texture for preview
        protected Texture2D previewTexture;
        
        public BaseNode()
        {
            NodeID = Guid.NewGuid().ToString();
            WindowRect = new Rect(100, 100, 150, 300);
        }
        
        public virtual void AddInputConnection(BaseNode fromNode, int fromPortIndex, int toPortIndex)
        {
            inputConnections.Add(new NodeConnection(fromNode.NodeID, fromPortIndex, NodeID, toPortIndex));
        }
        
        public virtual void AddOutputConnection(BaseNode toNode, int fromPortIndex, int toPortIndex)
        {
            outputConnections.Add(new NodeConnection(NodeID, fromPortIndex, toNode.NodeID, toPortIndex));
        }
        
        public virtual void RemoveConnection(NodeConnection connection)
        {
            inputConnections.RemoveAll(c => c.Equals(connection));
            outputConnections.RemoveAll(c => c.Equals(connection));
        }
        
        // Process the input textures and return the output texture
        public abstract Texture2D ProcessTexture(Dictionary<string, BaseNode> nodeMap);
        
        // Get preview texture (cached result)
        public Texture2D GetPreviewTexture()
        {
            return previewTexture;
        }
        
        // Set preview texture
        public void SetPreviewTexture(RenderTexture texture)
        {
            if (texture != null)
            {
                previewTexture = new Texture2D(texture.width, texture.height);
                RenderTexture.active = texture;
                previewTexture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                previewTexture.Apply();
                RenderTexture.active = null;
            }
        }
        
        // Process preview texture
        public abstract void ProcessPreview(RenderTexture input, RenderTexture output, Dictionary<string, BaseNode> nodeMap);
        
        // Display node in the editor window
        public virtual void DrawNode()
        {
            // This will be implemented in the editor script
        }
        
        // Get the input textures from connected nodes
        protected Texture2D GetInputTexture(int inputIndex, Dictionary<string, BaseNode> nodeMap)
        {
            foreach (var connection in inputConnections)
            {
                if (connection.ToPortIndex == inputIndex)
                {
                    if (nodeMap.TryGetValue(connection.FromNodeID, out BaseNode fromNode))
                    {
                        return fromNode.ProcessTexture(nodeMap);
                    }
                }
            }
            return null;
        }
    }
    
    [Serializable]
    public class NodeConnection
    {
        public string FromNodeID;
        public int FromPortIndex;
        public string ToNodeID;
        public int ToPortIndex;
        
        public NodeConnection(string fromNodeID, int fromPortIndex, string toNodeID, int toPortIndex)
        {
            FromNodeID = fromNodeID;
            FromPortIndex = fromPortIndex;
            ToNodeID = toNodeID;
            ToPortIndex = toPortIndex;
        }
        
        public override bool Equals(object obj)
        {
            if (obj is NodeConnection other)
            {
                return FromNodeID == other.FromNodeID &&
                       FromPortIndex == other.FromPortIndex &&
                       ToNodeID == other.ToNodeID &&
                       ToPortIndex == other.ToPortIndex;
            }
            return false;
        }
        
        public override int GetHashCode()
        {
            return (FromNodeID + FromPortIndex + ToNodeID + ToPortIndex).GetHashCode();
        }
    }
} 