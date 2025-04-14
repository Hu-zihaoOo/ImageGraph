using UnityEngine;
using System.Collections.Generic;
using NodeImageEditor.Interfaces;
using NodeImageEditor.Processors;

namespace NodeImageEditor
{
    public class BlendNode : BaseNode
    {
        private IImageProcessor blendProcessor;
        private BlendParameters blendParameters;

        public BlendMode BlendMode
        {
            get => blendParameters.Mode;
            set => blendParameters.Mode = value;
        }

        public float BlendStrength
        {
            get => blendParameters.Strength;
            set => blendParameters.Strength = value;
        }

        public BlendNode() : base()
        {
            Name = "Blend";
            blendProcessor = new BlendProcessor();
            blendParameters = new BlendParameters(BlendMode.Multiply, 1.0f);
        }

        public override Texture2D ProcessTexture(Dictionary<string, BaseNode> nodeMap)
        {
            Debug.Log($"BlendNode ProcessTexture called for node {NodeID}, input connections count: {inputConnections.Count}");
            
            // 打印所有输入连接的信息
            foreach (var conn in inputConnections)
            {
                Debug.Log($"BlendNode {NodeID} has input connection: FromNodeID={conn.FromNodeID}, FromPortIndex={conn.FromPortIndex}, ToPortIndex={conn.ToPortIndex}");
            }
            
            // 获取两个输入贴图
            Texture2D inputTexture1 = GetInputTexture(0, nodeMap);
            Texture2D inputTexture2 = GetInputTexture(1, nodeMap);
            
            // 打印输入信息
            Debug.Log($"BlendNode {NodeID}: Input1={inputTexture1 != null}, Input2={inputTexture2 != null}");
            
            // 检查输入是否有效
            if (inputTexture1 == null && inputTexture2 == null)
            {
                Debug.LogWarning($"BlendNode {NodeID}: No input textures found");
                if (previewTexture == null)
                {
                    previewTexture = new Texture2D(1, 1);
                    previewTexture.SetPixel(0, 0, Color.black);
                    previewTexture.Apply();
                }
                return previewTexture;
            }
            
            // 如果只有一个输入，返回那个输入
            if (inputTexture1 == null)
            {
                Debug.Log($"BlendNode {NodeID}: Using only input 2");
                return inputTexture2;
            }
            
            if (inputTexture2 == null)
            {
                Debug.Log($"BlendNode {NodeID}: Using only input 1");
                return inputTexture1;
            }

            Debug.Log($"BlendNode {NodeID}: Processing both textures with mode {BlendMode}");
            
            // 输出输入纹理的尺寸
            Debug.Log($"BlendNode {NodeID}: Input1 size={inputTexture1.width}x{inputTexture1.height}, Input2 size={inputTexture2.width}x{inputTexture2.height}");
            
            // 确保两个输入贴图尺寸一致 - 简单处理：使用第一个输入的尺寸为标准
            if (inputTexture1.width != inputTexture2.width || inputTexture1.height != inputTexture2.height)
            {
                Debug.LogWarning($"BlendNode {NodeID}: Input textures have different sizes. Adapting input 2 to match input 1.");
                // 这里可以做简单的尺寸匹配或者显示警告
            }
            
            // 使用处理器的特殊方法处理两个贴图
            BlendProcessor processor = blendProcessor as BlendProcessor;
            if (processor == null)
            {
                Debug.LogError($"BlendNode {NodeID}: Failed to get processor");
                return inputTexture1;
            }
            
            Texture2D resultTexture = processor.ProcessTextures(inputTexture1, inputTexture2, blendParameters);
            if (resultTexture == null)
            {
                Debug.LogError($"BlendNode {NodeID}: Failed to process textures");
                return inputTexture1;
            }

            Debug.Log($"BlendNode {NodeID}: Successfully processed textures, result size={resultTexture.width}x{resultTexture.height}");
            previewTexture = resultTexture;
            return resultTexture;
        }
        
        // 重写基类方法以确保正确处理输入连接
        protected new Texture2D GetInputTexture(int inputIndex, Dictionary<string, BaseNode> nodeMap)
        {
            Debug.Log($"BlendNode GetInputTexture called for port {inputIndex}, total connections: {inputConnections.Count}");
            
            foreach (var connection in inputConnections)
            {
                Debug.Log($"Checking connection: ToPortIndex={connection.ToPortIndex}, FromNodeID={connection.FromNodeID}");
                if (connection.ToPortIndex == inputIndex)
                {
                    if (nodeMap.TryGetValue(connection.FromNodeID, out BaseNode fromNode))
                    {
                        Debug.Log($"Found input for port {inputIndex} from node {fromNode.Name}");
                        var result = fromNode.ProcessTexture(nodeMap);
                        if (result == null)
                        {
                            Debug.LogWarning($"Node {fromNode.Name} returned null texture for port {inputIndex}");
                        }
                        return result;
                    }
                    else
                    {
                        Debug.LogWarning($"Could not find node with ID {connection.FromNodeID} in nodeMap");
                    }
                }
            }
            Debug.Log($"No input found for port {inputIndex}");
            return null;
        }

        public new void SetPreviewTexture(RenderTexture texture)
        {
            if (texture != null)
            {
                previewTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                RenderTexture.active = texture;
                previewTexture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                previewTexture.Apply();
                RenderTexture.active = null;
            }
        }

        public override void ProcessPreview(RenderTexture input, RenderTexture output, Dictionary<string, BaseNode> nodeMap)
        {
            // 我们需要两个输入贴图，所以这个默认方法不够用
            // 将在NodeBasedImageEditor中特殊处理
            Debug.Log($"BlendNode ProcessPreview called for node {NodeID} - DEFAULT METHOD, just copying input");
            Graphics.Blit(input, output); // 临时处理：如果只有一个输入，就直接复制
        }
        
        // 添加处理两个输入的特殊方法
        public void ProcessPreviewWithTwoInputs(RenderTexture input1, RenderTexture input2, RenderTexture output)
        {
            Debug.Log($"BlendNode ProcessPreviewWithTwoInputs called for node {NodeID} with BlendMode={BlendMode}, Strength={BlendStrength}");
            
            // 输出纹理信息，帮助调试
            Debug.Log($"Input1: {(input1 != null ? input1.width + "x" + input1.height : "null")}");
            Debug.Log($"Input2: {(input2 != null ? input2.width + "x" + input2.height : "null")}");
            Debug.Log($"Output: {(output != null ? output.width + "x" + output.height : "null")}");
            
            BlendProcessor processor = blendProcessor as BlendProcessor;
            if (processor != null)
            {
                processor.ProcessPreview(input1, input2, output, blendParameters);
            }
            else
            {
                Debug.LogError($"BlendNode {NodeID}: ProcessPreviewWithTwoInputs - processor is null");
                // 如果处理器不可用，至少显示第一个输入
                Graphics.Blit(input1, output);
            }
        }
    }
} 