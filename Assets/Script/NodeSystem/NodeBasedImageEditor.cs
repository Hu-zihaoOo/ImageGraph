using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NodeImageEditor
{
    public class NodeBasedImageEditor : EditorWindow
    {
        private NodeGraph graph;
        private BaseNode selectedNode;
        private List<BaseNode> nodesToDisplay = new List<BaseNode>();
        
        private bool isConnecting;
        private BaseNode connectionStartNode;
        private int connectionStartPort;
        
        private Vector2 scrollPosition;
        private Texture2D outputTexture;
        
        // Compute Shader相关
        private ComputeShader imageProcessingShader;
        private RenderTexture previewRenderTexture;
        private bool isPreviewDirty = true;
        private float lastPreviewUpdateTime;
        private const float PREVIEW_UPDATE_INTERVAL = 0.1f; // 预览更新间隔（秒）
        
        // 新增调整大小相关变量
        private Texture2D resizeIcon;
        private bool isResizing = false;
        private BaseNode resizingNode = null;
        private Vector2 resizeStartPosition;
        private Rect originalRect;
        private const float resizeHandleSize = 16;
        
        [MenuItem("Window/Node Based Image Editor")]
        public static void ShowWindow()
        {
            GetWindow<NodeBasedImageEditor>("Node Based Image Editor");
        }
        
        private void OnEnable()
        {
            if (graph == null)
            {
                graph = new NodeGraph();
            }
            
            // 加载Compute Shader
            imageProcessingShader = Resources.Load<ComputeShader>("ImageProcessingShader");
            if (imageProcessingShader == null)
            {
                Debug.LogError("Failed to load ImageProcessingShader. Please make sure it exists in Resources folder.");
            }
            
            // 创建预览RenderTexture
            CreatePreviewRenderTexture();
            
            // Example nodes for testing
            if (graph.GetAllNodes().Count == 0)
            {
                var inputNode = new InputImageNode();
                inputNode.WindowRect = new Rect(50, 50, 200, 350);
                graph.AddNode(inputNode);
                
                var colorNode = new ColorAdjustmentNode();
                colorNode.WindowRect = new Rect(300, 50, 200, 350);
                graph.AddNode(colorNode);
            }
            
            // 加载调整大小图标
            resizeIcon = EditorGUIUtility.IconContent("d_ScaleTool").image as Texture2D;
        }
        
        private void CreatePreviewRenderTexture()
        {
            if (previewRenderTexture != null)
            {
                previewRenderTexture.Release();
            }
            
            previewRenderTexture = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32);
            previewRenderTexture.enableRandomWrite = true;
            previewRenderTexture.Create();
        }
        
        private void OnDisable()
        {
            if (previewRenderTexture != null)
            {
                previewRenderTexture.Release();
                DestroyImmediate(previewRenderTexture);
            }
        }
        
        private void OnGUI()
        {
            DrawToolbar();
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            // 处理全局事件
            HandleGlobalEvents();
            
            // 更新预览
            UpdatePreviews();
            
            // 绘制UI元素
            DrawConnections();
            DrawNodes();
            
            // 处理特定节点事件
            HandleNodeEvents();
            
            // 处理调整大小的拖动效果
            if (isResizing && resizingNode != null)
            {
                DrawResizePreview();
            }
            
            EditorGUILayout.EndScrollView();
            
            if (outputTexture != null)
            {
                EditorGUILayout.LabelField("Output Preview", EditorStyles.boldLabel);
                Rect previewRect = GUILayoutUtility.GetRect(300, 300);
                GUI.DrawTexture(previewRect, outputTexture, ScaleMode.ScaleToFit);
                
                if (GUILayout.Button("Save Output Image"))
                {
                    SaveTextureToFile(outputTexture);
                }
            }
            
            // 确保拖动时持续更新
            if (isResizing || isConnecting)
            {
                Repaint();
            }
        }
        
        private void UpdatePreviews()
        {
            if (Time.realtimeSinceStartup - lastPreviewUpdateTime < PREVIEW_UPDATE_INTERVAL)
                return;

            lastPreviewUpdateTime = Time.realtimeSinceStartup;

            if (graph == null || imageProcessingShader == null)
                return;

            // 获取输入节点
            var inputNode = graph.GetAllNodes().FirstOrDefault(n => n is InputImageNode) as InputImageNode;
            if (inputNode == null || inputNode.Texture == null)
                return;

            // 创建预览纹理
            if (previewRenderTexture == null || previewRenderTexture.width != inputNode.Texture.width || previewRenderTexture.height != inputNode.Texture.height)
            {
                CreatePreviewRenderTexture();
            }

            // 创建节点输出的缓存字典，避免重复计算
            Dictionary<string, RenderTexture> nodeOutputs = new Dictionary<string, RenderTexture>();
            
            // 首先处理输入节点
            RenderTexture inputRT = RenderTexture.GetTemporary(previewRenderTexture.width, previewRenderTexture.height, 0, RenderTextureFormat.DefaultHDR);
            inputRT.enableRandomWrite = true;
            inputRT.Create();
            inputNode.ProcessPreview(null, inputRT, null);
            nodeOutputs[inputNode.NodeID] = inputRT;
            
            // 处理非BlendNode的节点
            ProcessNonBlendNodes(nodeOutputs);
            
            // 特殊处理BlendNode节点，因为它们需要两个输入
            ProcessBlendNodes(nodeOutputs);
            
            // 设置所有节点的预览
            foreach (var node in graph.GetAllNodes())
            {
                if (nodeOutputs.ContainsKey(node.NodeID))
                {
                    node.SetPreviewTexture(nodeOutputs[node.NodeID]);
                }
            }
            
            // 释放临时纹理
            foreach (var rt in nodeOutputs.Values)
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }
        
        private void ProcessNonBlendNodes(Dictionary<string, RenderTexture> nodeOutputs)
        {
            // 给每个连接的非Blend节点设置预览
            foreach (var node in graph.GetAllNodes())
            {
                // 跳过输入节点和Blend节点，它们会单独处理
                if (node is InputImageNode || node is BlendNode)
                    continue;
                    
                // 检查节点是否有输入连接
                bool hasInput = false;
                string inputNodeID = null;
                
                foreach (var conn in graph.GetAllConnections())
                {
                    if (conn.ToNodeID == node.NodeID)
                    {
                        hasInput = true;
                        inputNodeID = conn.FromNodeID;
                        break;
                    }
                }
                
                // 如果节点没有输入连接，跳过处理
                if (!hasInput || !nodeOutputs.ContainsKey(inputNodeID))
                {
                    continue;
                }
                
                // 获取输入纹理
                RenderTexture inputTexture = nodeOutputs[inputNodeID];
                
                // 创建节点输出的纹理
                RenderTexture outputTexture = RenderTexture.GetTemporary(previewRenderTexture.width, previewRenderTexture.height, 0, RenderTextureFormat.DefaultHDR);
                outputTexture.enableRandomWrite = true;
                outputTexture.Create();
                
                // 处理节点
                node.ProcessPreview(inputTexture, outputTexture, null);
                
                // 保存节点的输出
                nodeOutputs[node.NodeID] = outputTexture;
            }
        }
        
        private void ProcessBlendNodes(Dictionary<string, RenderTexture> nodeOutputs)
        {
            // 处理所有BlendNode节点
            foreach (var node in graph.GetAllNodes())
            {
                if (!(node is BlendNode blendNode))
                    continue;
                
                Debug.Log($"Processing BlendNode {node.NodeID}");
                    
                // 查找两个输入连接
                string input1NodeID = null;
                string input2NodeID = null;
                
                // 列出所有连接用于调试
                Debug.Log($"All connections in graph: {graph.GetAllConnections().Count}");
                foreach (var conn in graph.GetAllConnections())
                {
                    Debug.Log($"Connection: FromNodeID={conn.FromNodeID}, FromPortIndex={conn.FromPortIndex}, ToNodeID={conn.ToNodeID}, ToPortIndex={conn.ToPortIndex}");
                }
                
                // 查找连接到当前BlendNode的输入
                foreach (var conn in graph.GetAllConnections())
                {
                    if (conn.ToNodeID == node.NodeID)
                    {
                        Debug.Log($"Found connection to BlendNode: ToPortIndex={conn.ToPortIndex}, FromNodeID={conn.FromNodeID}");
                        if (conn.ToPortIndex == 0)
                        {
                            input1NodeID = conn.FromNodeID;
                            Debug.Log($"Assigned input1NodeID = {input1NodeID}");
                        }
                        else if (conn.ToPortIndex == 1)
                        {
                            input2NodeID = conn.FromNodeID;
                            Debug.Log($"Assigned input2NodeID = {input2NodeID}");
                        }
                    }
                }
                
                // 检查是否有足够的输入
                bool hasInput1 = input1NodeID != null && nodeOutputs.ContainsKey(input1NodeID);
                bool hasInput2 = input2NodeID != null && nodeOutputs.ContainsKey(input2NodeID);
                
                // 输出详细调试信息
                Debug.Log($"BlendNode {node.NodeID}: input1NodeID={input1NodeID}, exists in outputs={input1NodeID != null && nodeOutputs.ContainsKey(input1NodeID)}");
                Debug.Log($"BlendNode {node.NodeID}: input2NodeID={input2NodeID}, exists in outputs={input2NodeID != null && nodeOutputs.ContainsKey(input2NodeID)}");
                
                // 输出nodeOutputs中的所有键
                string outputKeys = "NodeOutputs keys: ";
                foreach (var key in nodeOutputs.Keys)
                {
                    outputKeys += key + ", ";
                }
                Debug.Log(outputKeys);
                
                // 如果没有任何输入，跳过处理
                if (!hasInput1 && !hasInput2)
                {
                    Debug.LogWarning($"BlendNode {node.NodeID}: No valid inputs found");
                    continue;
                }
                
                // 创建节点输出的纹理
                RenderTexture outputTexture = RenderTexture.GetTemporary(previewRenderTexture.width, previewRenderTexture.height, 0, RenderTextureFormat.DefaultHDR);
                outputTexture.enableRandomWrite = true;
                outputTexture.Create();
                
                // 如果有两个输入，使用特殊处理方法进行混合
                if (hasInput1 && hasInput2)
                {
                    Debug.Log($"BlendNode {node.NodeID}: Processing with two inputs");
                    RenderTexture input1Texture = nodeOutputs[input1NodeID];
                    RenderTexture input2Texture = nodeOutputs[input2NodeID];
                    
                    if (input1Texture == null || input2Texture == null)
                    {
                        Debug.LogError($"BlendNode {node.NodeID}: One of the input textures is null despite being in nodeOutputs");
                        if (input1Texture != null)
                            Graphics.Blit(input1Texture, outputTexture);
                        else if (input2Texture != null)
                            Graphics.Blit(input2Texture, outputTexture);
                        nodeOutputs[node.NodeID] = outputTexture;
                        continue;
                    }
                    
                    Debug.Log($"Input1: {input1Texture.width}x{input1Texture.height}, Input2: {input2Texture.width}x{input2Texture.height}");
                    
                    // 使用特殊方法处理两个输入
                    try
                    {
                        blendNode.ProcessPreviewWithTwoInputs(input1Texture, input2Texture, outputTexture);
                        Debug.Log($"BlendNode {node.NodeID}: Successfully processed with ProcessPreviewWithTwoInputs");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"BlendNode {node.NodeID}: Error in ProcessPreviewWithTwoInputs: {e.Message}");
                        // 出错时使用简单的混合
                        Graphics.Blit(input1Texture, outputTexture);
                    }
                }
                else if (hasInput1)
                {
                    // 只有一个输入，直接复制
                    Debug.Log($"BlendNode {node.NodeID}: Only input 1 available, copying");
                    Graphics.Blit(nodeOutputs[input1NodeID], outputTexture);
                }
                else if (hasInput2)
                {
                    // 只有一个输入，直接复制
                    Debug.Log($"BlendNode {node.NodeID}: Only input 2 available, copying");
                    Graphics.Blit(nodeOutputs[input2NodeID], outputTexture);
                }
                
                // 保存节点的输出
                nodeOutputs[node.NodeID] = outputTexture;
                Debug.Log($"BlendNode {node.NodeID}: Added output texture to nodeOutputs");
            }
        }
        
        private void ProcessTexture(Texture2D input, RenderTexture output)
        {
            if (imageProcessingShader == null)
                return;

            // 处理整个节点图
            Dictionary<string, BaseNode> nodeMap = new Dictionary<string, BaseNode>();
            foreach (var node in graph.GetAllNodes())
            {
                nodeMap[node.NodeID] = node;
            }
            
            // 获取最后一个节点的输出
            BaseNode outputNode = null;
            foreach (var node in graph.GetAllNodes())
            {
                if (node is BlurNode || node is ColorAdjustmentNode || node is TillingOffsetNode || node is BlendNode)
                {
                    if (outputNode == null || node.NodeID.CompareTo(outputNode.NodeID) > 0)
                    {
                        outputNode = node;
                    }
                }
            }
            
            if (outputNode != null)
            {
                Texture2D processedTexture = outputNode.ProcessTexture(nodeMap);
                if (processedTexture != null)
                {
                    // 将处理后的纹理复制到输出RenderTexture
                    Graphics.Blit(processedTexture, output);
                    return;
                }
            }
            
            // 如果没有处理节点或处理失败，使用默认处理
            int kernelIndex = imageProcessingShader.FindKernel("CSMain");
            imageProcessingShader.SetTexture(kernelIndex, "InputTexture", input);
            imageProcessingShader.SetTexture(kernelIndex, "Result", output);

            int threadGroupsX = Mathf.CeilToInt(output.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(output.height / 8.0f);
            imageProcessingShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);
        }
        
        
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            if (GUILayout.Button("Add Input Node", EditorStyles.toolbarButton))
            {
                var inputNode = new InputImageNode();
                inputNode.WindowRect = new Rect(50, 50, 200, 350); // 确保初始高度足够
                graph.AddNode(inputNode);
            }
            
            if (GUILayout.Button("Add Color Adjustment Node", EditorStyles.toolbarButton))
            {
                var colorNode = new ColorAdjustmentNode();
                colorNode.WindowRect = new Rect(300, 50, 200, 350); // 确保初始高度足够
                graph.AddNode(colorNode);
            }
            
            if (GUILayout.Button("Add Blur Node", EditorStyles.toolbarButton))
            {
                var blurNode = new BlurNode();
                blurNode.WindowRect = new Rect(300, 50, 200, 350);
                graph.AddNode(blurNode);
            }
            
            if (GUILayout.Button("Add Tilling & Offset Node", EditorStyles.toolbarButton))
            {
                var tillingNode = new TillingOffsetNode();
                tillingNode.WindowRect = new Rect(300, 50, 200, 350);
                graph.AddNode(tillingNode);
            }
            
            if (GUILayout.Button("Add Blend Node", EditorStyles.toolbarButton))
            {
                var blendNode = new BlendNode();
                blendNode.WindowRect = new Rect(300, 50, 200, 350);
                graph.AddNode(blendNode);
            }
            
            if (GUILayout.Button("Process Graph", EditorStyles.toolbarButton))
            {
                ProcessSelectedNode();
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawNodes()
        {
            nodesToDisplay = graph.GetAllNodes();
            
            BeginWindows();
            
            for (int i = 0; i < nodesToDisplay.Count; i++)
            {
                BaseNode node = nodesToDisplay[i];
                
                // 绘制节点窗口
                EditorGUI.BeginChangeCheck();
                node.WindowRect = GUI.Window(i, node.WindowRect, DrawNodeWindow, node.Name);
                if (EditorGUI.EndChangeCheck())
                {
                    // 窗口被移动了，更新预览图
                    Repaint();
                }
                
                // 检查是否需要绘制调整大小的预览
                if (isResizing && resizingNode == node)
                {
                    // 绘制大小调整时的视觉反馈
                    Handles.BeginGUI();
                    Handles.color = new Color(1, 1, 0, 0.5f);
                    Handles.DrawSolidRectangleWithOutline(
                        new Rect(node.WindowRect.x, node.WindowRect.y, node.WindowRect.width, node.WindowRect.height),
                        new Color(1, 1, 0, 0.1f),
                        new Color(1, 1, 0, 0.6f));
                    Handles.EndGUI();
                }
            }
            
            EndWindows();
        }
        
        private void DrawNodeWindow(int id)
        {
            BaseNode node = nodesToDisplay[id];
            
            // 记录原始高度
            float originalHeight = node.WindowRect.height;
            
            // 开始计算新的高度，设置最小高度
            float currentHeight = 0;
            float contentWidth = node.WindowRect.width - 20; // 留出边距
            float minimumHeight = 250; // 确保窗口有最小高度
            
            GUILayout.BeginVertical();
            
            // Draw the node content based on its type
            if (node is InputImageNode inputNode)
            {
                // 标题区域
                GUILayout.Label(node.Name, EditorStyles.boldLabel);
                currentHeight += EditorGUIUtility.singleLineHeight + 5;
                
                // Input image settings area
                GUILayout.BeginVertical(GUI.skin.box);
                EditorGUI.BeginChangeCheck();
                inputNode.Texture = (Texture2D)EditorGUILayout.ObjectField("Texture", inputNode.Texture, typeof(Texture2D), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Repaint();
                }
                GUILayout.EndVertical();
                currentHeight += EditorGUIUtility.singleLineHeight * 2 + 10; // 对象字段的高度
                
                // 计算端口位置 - 使用当前积累的高度
                float portPositionY = Mathf.Min(70, originalHeight * 0.3f);
                
                // Ports are drawn outside the GUILayout to ensure fixed positions
                // Output port at right side with improved visuals
                Rect outputRect = new Rect(node.WindowRect.width - 12, portPositionY, 24, 24);
                Color oldColor = GUI.color;
                // Use different colors when connecting
                if (isConnecting)
                {
                    if (connectionStartNode == node)
                        GUI.color = Color.yellow; // Active source port
                    else
                        GUI.color = Color.green; // Potential target port
                }
                else
                {
                    GUI.color = new Color(0.8f, 0.8f, 0.2f); // Default color
                }
                
                GUI.DrawTexture(outputRect, EditorGUIUtility.IconContent("d_GridLayoutGroup Icon").image);
                GUI.color = oldColor;
                
                // Invisible button for interaction
                if (GUI.Button(outputRect, "", GUIStyle.none))
                {
                    HandlePortClick(node, 0, true); // true = output port
                }
                
                // Preview area
                if (inputNode.GetPreviewTexture() != null)
                {
                    GUILayout.Box("Preview", GUILayout.ExpandWidth(true));
                    Rect previewRect = GUILayoutUtility.GetRect(contentWidth, 150);
                    GUI.DrawTexture(previewRect, inputNode.GetPreviewTexture(), ScaleMode.ScaleToFit);
                    currentHeight += 170; // 预览标题和图像的高度
                }
                
                // 确保有足够空间给按钮
                GUILayout.Space(20);
                currentHeight += 20;
            }
            else if (node is ColorAdjustmentNode colorNode)
            {
                // 标题区域
                GUILayout.Label(node.Name, EditorStyles.boldLabel);
                currentHeight += EditorGUIUtility.singleLineHeight + 5;
                
                // Color adjustment sliders area
                GUILayout.BeginVertical(GUI.skin.box);
                EditorGUI.BeginChangeCheck();
                colorNode.Brightness = EditorGUILayout.Slider("Brightness", colorNode.Brightness, 0, 2);
                colorNode.Contrast = EditorGUILayout.Slider("Contrast", colorNode.Contrast, 0, 2);
                colorNode.Saturation = EditorGUILayout.Slider("Saturation", colorNode.Saturation, 0, 2);
                colorNode.Hue = EditorGUILayout.Slider("Hue", colorNode.Hue, 0, 1);
                if (EditorGUI.EndChangeCheck())
                {
                    Repaint();
                }
                GUILayout.EndVertical();
                currentHeight += EditorGUIUtility.singleLineHeight * 5 + 15; // 4个滑块 + 边距
                
                // 计算端口位置 - 使用当前积累的高度
                float portPositionY = Mathf.Min(70, originalHeight * 0.3f);
                
                // Ports are drawn outside the GUILayout to ensure fixed positions
                // Input port at left side with improved visuals
                Rect inputRect = new Rect(-12, portPositionY, 24, 24);
                Color oldColor = GUI.color;
                // Use different colors when connecting
                if (isConnecting)
                {
                    if (connectionStartNode == node && connectionStartPort == 0)
                        GUI.color = Color.yellow; // Active source port
                    else if (connectionStartNode != node)
                        GUI.color = Color.green; // Potential target port
                    else
                        GUI.color = new Color(0.2f, 0.6f, 0.8f); // Default input color
                }
                else
                {
                    GUI.color = new Color(0.2f, 0.6f, 0.8f); // Default input color
                }
                
                GUI.DrawTexture(inputRect, EditorGUIUtility.IconContent("d_GridLayoutGroup Icon").image);
                GUI.color = oldColor;
                
                // Invisible button for interaction
                if (GUI.Button(inputRect, "", GUIStyle.none))
                {
                    HandlePortClick(node, 0, false); // false = input port
                }
                
                // Output port at right side with improved visuals
                Rect outputRect = new Rect(node.WindowRect.width - 12, portPositionY, 24, 24);
                oldColor = GUI.color;
                // Use different colors when connecting
                if (isConnecting)
                {
                    if (connectionStartNode == node && connectionStartPort == 1)
                        GUI.color = Color.yellow; // Active source port
                    else if (connectionStartNode != node)
                        GUI.color = Color.green; // Potential target port
                    else
                        GUI.color = new Color(0.8f, 0.8f, 0.2f); // Default output color
                }
                else
                {
                    GUI.color = new Color(0.8f, 0.8f, 0.2f); // Default output color
                }
                
                GUI.DrawTexture(outputRect, EditorGUIUtility.IconContent("d_GridLayoutGroup Icon").image);
                GUI.color = oldColor;
                
                // Invisible button for interaction
                if (GUI.Button(outputRect, "", GUIStyle.none))
                {
                    HandlePortClick(node, 1, true); // true = output port
                }
                
                // Preview area
                if (colorNode.GetPreviewTexture() != null)
                {
                    GUILayout.Box("Preview", GUILayout.ExpandWidth(true));
                    Rect previewRect = GUILayoutUtility.GetRect(contentWidth, 150);
                    GUI.DrawTexture(previewRect, colorNode.GetPreviewTexture(), ScaleMode.ScaleToFit);
                    currentHeight += 170; // 预览标题和图像的高度
                }
            }
            else if (node is BlurNode blurNode)
            {
                // 标题区域
                GUILayout.Label(node.Name, EditorStyles.boldLabel);
                currentHeight += EditorGUIUtility.singleLineHeight + 5;
                
                // Blur adjustment area
                GUILayout.BeginVertical(GUI.skin.box);
                EditorGUI.BeginChangeCheck();
                blurNode.BlurRadius = EditorGUILayout.IntSlider("Blur Radius", blurNode.BlurRadius, 1, 10);
                if (EditorGUI.EndChangeCheck())
                {
                    Repaint();
                }
                GUILayout.EndVertical();
                currentHeight += EditorGUIUtility.singleLineHeight * 2 + 10; // 滑块高度 + 边距
                
                // 计算端口位置
                float portPositionY = Mathf.Min(70, originalHeight * 0.3f);
                
                // 输入端口
                Rect inputRect = new Rect(-12, portPositionY, 24, 24);
                Color oldColor = GUI.color;
                if (isConnecting)
                {
                    if (connectionStartNode == node && connectionStartPort == 0)
                        GUI.color = Color.yellow;
                    else if (connectionStartNode != node)
                        GUI.color = Color.green;
                    else
                        GUI.color = new Color(0.2f, 0.6f, 0.8f);
                }
                else
                {
                    GUI.color = new Color(0.2f, 0.6f, 0.8f);
                }
                
                GUI.DrawTexture(inputRect, EditorGUIUtility.IconContent("d_GridLayoutGroup Icon").image);
                GUI.color = oldColor;
                
                if (GUI.Button(inputRect, "", GUIStyle.none))
                {
                    HandlePortClick(node, 0, false);
                }
                
                // 输出端口
                Rect outputRect = new Rect(node.WindowRect.width - 12, portPositionY, 24, 24);
                oldColor = GUI.color;
                if (isConnecting)
                {
                    if (connectionStartNode == node && connectionStartPort == 1)
                        GUI.color = Color.yellow;
                    else if (connectionStartNode != node)
                        GUI.color = Color.green;
                    else
                        GUI.color = new Color(0.8f, 0.8f, 0.2f);
                }
                else
                {
                    GUI.color = new Color(0.8f, 0.8f, 0.2f);
                }
                
                GUI.DrawTexture(outputRect, EditorGUIUtility.IconContent("d_GridLayoutGroup Icon").image);
                GUI.color = oldColor;
                
                if (GUI.Button(outputRect, "", GUIStyle.none))
                {
                    HandlePortClick(node, 1, true);
                }
                
                // 预览区域
                if (blurNode.GetPreviewTexture() != null)
                {
                    GUILayout.Box("Preview", GUILayout.ExpandWidth(true));
                    Rect previewRect = GUILayoutUtility.GetRect(contentWidth, 150);
                    GUI.DrawTexture(previewRect, blurNode.GetPreviewTexture(), ScaleMode.ScaleToFit);
                    currentHeight += 170;
                }
            }
            else if (node is TillingOffsetNode tillingNode)
            {
                // 标题区域
                GUILayout.Label(node.Name, EditorStyles.boldLabel);
                currentHeight += EditorGUIUtility.singleLineHeight + 5;
                
                // Tilling & Offset adjustment area
                GUILayout.BeginVertical(GUI.skin.box);
                EditorGUI.BeginChangeCheck();
                tillingNode.TillingX = EditorGUILayout.FloatField("Tilling X", tillingNode.TillingX);
                tillingNode.TillingY = EditorGUILayout.FloatField("Tilling Y", tillingNode.TillingY);
                tillingNode.OffsetX = EditorGUILayout.FloatField("Offset X", tillingNode.OffsetX);
                tillingNode.OffsetY = EditorGUILayout.FloatField("Offset Y", tillingNode.OffsetY);
                if (EditorGUI.EndChangeCheck())
                {
                    Repaint();
                }
                GUILayout.EndVertical();
                currentHeight += EditorGUIUtility.singleLineHeight * 5 + 15;
                
                // 计算端口位置
                float portPositionY = Mathf.Min(70, originalHeight * 0.3f);
                
                // 输入端口
                Rect inputRect = new Rect(-12, portPositionY, 24, 24);
                Color oldColor = GUI.color;
                if (isConnecting)
                {
                    if (connectionStartNode == node && connectionStartPort == 0)
                        GUI.color = Color.yellow;
                    else if (connectionStartNode != node)
                        GUI.color = Color.green;
                    else
                        GUI.color = new Color(0.2f, 0.6f, 0.8f);
                }
                else
                {
                    GUI.color = new Color(0.2f, 0.6f, 0.8f);
                }
                
                GUI.DrawTexture(inputRect, EditorGUIUtility.IconContent("d_GridLayoutGroup Icon").image);
                GUI.color = oldColor;
                
                if (GUI.Button(inputRect, "", GUIStyle.none))
                {
                    HandlePortClick(node, 0, false);
                }
                
                // 输出端口
                Rect outputRect = new Rect(node.WindowRect.width - 12, portPositionY, 24, 24);
                oldColor = GUI.color;
                if (isConnecting)
                {
                    if (connectionStartNode == node && connectionStartPort == 1)
                        GUI.color = Color.yellow;
                    else if (connectionStartNode != node)
                        GUI.color = Color.green;
                    else
                        GUI.color = new Color(0.8f, 0.8f, 0.2f);
                }
                else
                {
                    GUI.color = new Color(0.8f, 0.8f, 0.2f);
                }
                
                GUI.DrawTexture(outputRect, EditorGUIUtility.IconContent("d_GridLayoutGroup Icon").image);
                GUI.color = oldColor;
                
                if (GUI.Button(outputRect, "", GUIStyle.none))
                {
                    HandlePortClick(node, 1, true);
                }
                
                // 预览区域
                if (tillingNode.GetPreviewTexture() != null)
                {
                    GUILayout.Box("Preview", GUILayout.ExpandWidth(true));
                    Rect previewRect = GUILayoutUtility.GetRect(contentWidth, 150);
                    GUI.DrawTexture(previewRect, tillingNode.GetPreviewTexture(), ScaleMode.ScaleToFit);
                    currentHeight += 170;
                }
            }
            else if (node is BlendNode blendNode)
            {
                // 标题区域
                GUILayout.Label(node.Name, EditorStyles.boldLabel);
                currentHeight += EditorGUIUtility.singleLineHeight + 5;
                
                // Blend adjustment area
                GUILayout.BeginVertical(GUI.skin.box);
                EditorGUI.BeginChangeCheck();
                blendNode.BlendMode = (Processors.BlendMode)EditorGUILayout.EnumPopup("Blend Mode", blendNode.BlendMode);
                blendNode.BlendStrength = EditorGUILayout.Slider("Blend Strength", blendNode.BlendStrength, 0.0f, 2.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    Repaint();
                }
                GUILayout.EndVertical();
                currentHeight += EditorGUIUtility.singleLineHeight * 3 + 12;
                
                // 计算端口位置 - 两个输入端口和一个输出端口
                float port1Y = Mathf.Min(50, originalHeight * 0.2f);
                float port2Y = Mathf.Min(90, originalHeight * 0.4f);
                float outputPortY = Mathf.Min(70, originalHeight * 0.3f);
                
                // 第一个输入端口
                Rect input1Rect = new Rect(-12, port1Y, 24, 24);
                Color oldColor = GUI.color;
                if (isConnecting)
                {
                    if (connectionStartNode == node && connectionStartPort == 0)
                        GUI.color = Color.yellow;
                    else if (connectionStartNode != node)
                        GUI.color = Color.green;
                    else
                        GUI.color = new Color(0.2f, 0.6f, 0.8f);
                }
                else
                {
                    GUI.color = new Color(0.2f, 0.6f, 0.8f);
                }
                
                GUI.DrawTexture(input1Rect, EditorGUIUtility.IconContent("d_GridLayoutGroup Icon").image);
                GUI.Label(new Rect(15, port1Y, 50, 20), "In 1");
                GUI.color = oldColor;
                
                if (GUI.Button(input1Rect, "", GUIStyle.none))
                {
                    HandlePortClick(node, 0, false);
                }
                
                // 第二个输入端口
                Rect input2Rect = new Rect(-12, port2Y, 24, 24);
                oldColor = GUI.color;
                if (isConnecting)
                {
                    if (connectionStartNode == node && connectionStartPort == 1)
                        GUI.color = Color.yellow;
                    else if (connectionStartNode != node)
                        GUI.color = Color.green;
                    else
                        GUI.color = new Color(0.2f, 0.6f, 0.8f);
                }
                else
                {
                    GUI.color = new Color(0.2f, 0.6f, 0.8f);
                }
                
                GUI.DrawTexture(input2Rect, EditorGUIUtility.IconContent("d_GridLayoutGroup Icon").image);
                GUI.Label(new Rect(15, port2Y, 50, 20), "In 2");
                GUI.color = oldColor;
                
                if (GUI.Button(input2Rect, "", GUIStyle.none))
                {
                    HandlePortClick(node, 1, false);
                }
                
                // 输出端口
                Rect outputRect = new Rect(node.WindowRect.width - 12, outputPortY, 24, 24);
                oldColor = GUI.color;
                if (isConnecting)
                {
                    if (connectionStartNode == node && connectionStartPort == 2)
                        GUI.color = Color.yellow;
                    else if (connectionStartNode != node)
                        GUI.color = Color.green;
                    else
                        GUI.color = new Color(0.8f, 0.8f, 0.2f);
                }
                else
                {
                    GUI.color = new Color(0.8f, 0.8f, 0.2f);
                }
                
                GUI.DrawTexture(outputRect, EditorGUIUtility.IconContent("d_GridLayoutGroup Icon").image);
                GUI.color = oldColor;
                
                if (GUI.Button(outputRect, "", GUIStyle.none))
                {
                    HandlePortClick(node, 2, true);
                }
                
                // 预览区域
                if (blendNode.GetPreviewTexture() != null)
                {
                    GUILayout.Box("Preview", GUILayout.ExpandWidth(true));
                    Rect previewRect = GUILayoutUtility.GetRect(contentWidth, 150);
                    GUI.DrawTexture(previewRect, blendNode.GetPreviewTexture(), ScaleMode.ScaleToFit);
                    currentHeight += 170;
                }
            }
            
            GUILayout.Space(5);
            currentHeight += 5;
            
            // Action buttons area - 确保在底部有足够空间
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Process"))
            {
                selectedNode = node;
                ProcessSelectedNode();
            }
            
            if (GUILayout.Button("Delete"))
            {
                graph.RemoveNode(node.NodeID);
            }
            GUILayout.EndHorizontal();
            currentHeight += EditorGUIUtility.singleLineHeight + 10; // 按钮高度 + 边距
            
            GUILayout.EndVertical();
            
            // 自动调整节点高度（仅当不在调整大小时）
            if (!isResizing || resizingNode != node)
            {
                currentHeight += 20; // 添加额外边距
                // 确保高度不小于最小高度
                currentHeight = Mathf.Max(currentHeight, minimumHeight);
                
                if (Mathf.Abs(currentHeight - originalHeight) > 5)
                {
                    // 如果高度差异较大，则更新
                    node.WindowRect.height = currentHeight;
                    Repaint();
                }
            }
            
            // 绘制调整大小的手柄
            Rect resizeHandleRect = new Rect(
                node.WindowRect.width - resizeHandleSize, 
                node.WindowRect.height - resizeHandleSize,
                resizeHandleSize,
                resizeHandleSize);
            
            GUI.color = resizingNode == node ? Color.yellow : Color.white;
            GUI.DrawTexture(resizeHandleRect, resizeIcon);
            GUI.color = Color.white;
            
            // 为调整大小区域添加鼠标指针变化
            EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeUpLeft);
            
            // 仅当不是调整大小时，才允许整个窗口拖动
            if (!(isResizing && resizingNode == node))
            {
                GUI.DragWindow();
            }
        }
        
        // 处理端口点击的新方法，简化连接逻辑
        private void HandlePortClick(BaseNode node, int portIndex, bool isOutput)
        {
            if (isConnecting)
            {
                // 已经在连接状态，检查是否可以完成连接
                bool canConnect = connectionStartNode != node; // 不能连接到自己
                
                // 检查是否是有效的输入/输出组合
                bool validConnection = false;
                
                if (canConnect)
                {
                    // 输出端口连接到输入端口
                    if (connectionStartNode != null && isConnectionFromOutputPort() && !isOutput)
                    {
                        // 从输出连到输入
                        graph.ConnectNodes(connectionStartNode.NodeID, connectionStartPort, node.NodeID, portIndex);
                        validConnection = true;
                    }
                    // 输入端口连接到输出端口
                    else if (connectionStartNode != null && !isConnectionFromOutputPort() && isOutput)
                    {
                        // 从输入连到输出
                        graph.ConnectNodes(node.NodeID, portIndex, connectionStartNode.NodeID, connectionStartPort);
                        validConnection = true;
                    }
                }
                
                // 重置连接状态
                isConnecting = !validConnection;
                if (!isConnecting)
                {
                    connectionStartNode = null;
                    connectionStartPort = -1;
                }
            }
            else
            {
                // 开始新的连接
                isConnecting = true;
                connectionStartNode = node;
                connectionStartPort = portIndex;
            }
            
            Repaint();
        }
        
        // 判断当前连接是否从输出端口开始
        private bool isConnectionFromOutputPort()
        {
            if (connectionStartNode is InputImageNode)
            {
                return true; // InputImageNode只有输出端口
            }
            else if (connectionStartNode is ColorAdjustmentNode || connectionStartNode is BlurNode || connectionStartNode is TillingOffsetNode)
            {
                return connectionStartPort == 1; // 这些节点的输出端口索引为1
            }
            else if (connectionStartNode is BlendNode)
            {
                return connectionStartPort == 2; // BlendNode的输出端口索引为2
            }
            return false;
        }
        
        private void DrawConnections()
        {
            // Set up styles for connections
            Texture2D connectionTexture = GetConnectionGradientTexture();
            
            List<NodeConnection> connections = graph.GetAllConnections();
            
            // 检查是否点击了连接线以删除连接
            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 1)
            {
                // 检查是否点击了某条连接线
                for (int i = 0; i < connections.Count; i++)
                {
                    var connection = connections[i];
                    BaseNode fromNode = graph.GetNodeByID(connection.FromNodeID);
                    BaseNode toNode = graph.GetNodeByID(connection.ToNodeID);
                    
                    if (fromNode != null && toNode != null)
                    {
                        // 计算端口位置
                        Vector2 startPos = GetPortPosition(fromNode, connection.FromPortIndex, true);
                        Vector2 endPos = GetPortPosition(toNode, connection.ToPortIndex, false);
                        
                        // 计算控制点
                        Vector2 startTangent = startPos + Vector2.right * 60;
                        Vector2 endTangent = endPos + Vector2.left * 60;
                        
                        // 判断是否点击了贝塞尔曲线
                        if (IsPointNearBezier(currentEvent.mousePosition, startPos, endPos, startTangent, endTangent, 10f))
                        {
                            // 显示右键菜单
                            GenericMenu menu = new GenericMenu();
                            menu.AddItem(new GUIContent("Delete Connection"), false, () => {
                                DeleteConnection(connection);
                            });
                            menu.ShowAsContext();
                            currentEvent.Use();
                            break;
                        }
                    }
                }
            }
            
            foreach (var connection in connections)
            {
                BaseNode fromNode = graph.GetNodeByID(connection.FromNodeID);
                BaseNode toNode = graph.GetNodeByID(connection.ToNodeID);
                
                if (fromNode != null && toNode != null)
                {
                    // 获取端口位置
                    Vector2 startPos = GetPortPosition(fromNode, connection.FromPortIndex, true);
                    Vector2 endPos = GetPortPosition(toNode, connection.ToPortIndex, false);
                    
                    // Draw line with more appealing style
                    Handles.BeginGUI();
                    
                    // Draw a shadow
                    Color shadowColor = new Color(0, 0, 0, 0.2f);
                    Handles.color = shadowColor;
                    Handles.DrawBezier(
                        startPos + new Vector2(2, 2), 
                        endPos + new Vector2(2, 2),
                        startPos + Vector2.right * 60,
                        endPos + Vector2.left * 60,
                        shadowColor, connectionTexture, 4);
                    
                    // Draw main line
                    Handles.color = new Color(0.8f, 0.8f, 0.8f, 0.8f);
                    Handles.DrawBezier(
                        startPos, endPos,
                        startPos + Vector2.right * 60,
                        endPos + Vector2.left * 60,
                        Handles.color, connectionTexture, 3);
                    
                    Handles.EndGUI();
                }
            }
            
            // Draw in-progress connection
            if (isConnecting)
            {
                Vector2 startPos;
                
                bool isFromOutput = isConnectionFromOutputPort();
                
                // 计算开始位置
                if (isFromOutput)
                {
                    // 从输出端口开始连接
                    startPos = GetPortPosition(connectionStartNode, connectionStartPort, true);
                }
                else
                {
                    // 从输入端口开始连接
                    startPos = GetPortPosition(connectionStartNode, connectionStartPort, false);
                }
                
                Vector2 endPos = Event.current.mousePosition;
                
                // Draw active connection line with animation
                Handles.BeginGUI();
                Handles.color = Color.yellow;
                
                float pulseSpeed = 1.0f;
                float pulseAmount = Mathf.Sin(Time.realtimeSinceStartup * pulseSpeed * 5) * 0.5f + 1.5f;
                
                Handles.DrawBezier(
                    startPos, endPos,
                    startPos + (isFromOutput ? Vector2.right : Vector2.left) * 60,
                    endPos + (isFromOutput ? Vector2.left : Vector2.right) * 60,
                    Handles.color, connectionTexture, 2 * pulseAmount);
                Handles.EndGUI();
                
                Repaint();
            }
        }
        
        // 删除连接
        private void DeleteConnection(NodeConnection connection)
        {
            // 从图中删除连接
            graph.DisconnectNodes(connection.FromNodeID, connection.FromPortIndex, connection.ToNodeID, connection.ToPortIndex);
            Repaint();
        }
        
        // 判断点是否靠近贝塞尔曲线的辅助方法
        private bool IsPointNearBezier(Vector2 point, Vector2 startPos, Vector2 endPos, Vector2 startTangent, Vector2 endTangent, float threshold)
        {
            // 贝塞尔曲线采样点数量
            const int samples = 10;
            
            // 使用线性近似来判断点是否靠近曲线
            Vector2 prevPoint = CubicBezierPoint(0, startPos, startTangent, endTangent, endPos);
            
            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector2 currentPoint = CubicBezierPoint(t, startPos, startTangent, endTangent, endPos);
                
                // 计算点到线段的距离
                float distance = DistancePointToLine(point, prevPoint, currentPoint);
                if (distance < threshold)
                    return true;
                
                prevPoint = currentPoint;
            }
            
            return false;
        }
        
        // 贝塞尔曲线插值计算
        private Vector2 CubicBezierPoint(float t, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;
            
            Vector2 p = uuu * p0; // (1-t)³ * P0
            p += 3 * uu * t * p1; // 3(1-t)² * t * P1
            p += 3 * u * tt * p2; // 3(1-t) * t² * P2
            p += ttt * p3; // t³ * P3
            
            return p;
        }
        
        // 计算点到线段的距离
        private float DistancePointToLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            // 计算线段向量
            Vector2 lineVec = lineEnd - lineStart;
            float lineLength = lineVec.magnitude;
            
            if (lineLength < 0.0001f)
                return Vector2.Distance(point, lineStart); // 线段退化为点
                
            // 标准化线段向量
            Vector2 lineDir = lineVec / lineLength;
            
            // 计算点到线段起点的向量
            Vector2 pointVec = point - lineStart;
            
            // 计算点在线段上的投影长度
            float projection = Vector2.Dot(pointVec, lineDir);
            
            // 如果投影在线段外，返回点到端点的距离
            if (projection < 0)
                return Vector2.Distance(point, lineStart);
            else if (projection > lineLength)
                return Vector2.Distance(point, lineEnd);
                
            // 计算投影点
            Vector2 projectionPoint = lineStart + lineDir * projection;
            
            // 返回点到投影点的距离
            return Vector2.Distance(point, projectionPoint);
        }
        
        // 创建连接线的渐变纹理
        private Texture2D GetConnectionGradientTexture()
        {
            if (_connectionTexture == null)
            {
                _connectionTexture = new Texture2D(1, 3, TextureFormat.RGBA32, false);
                _connectionTexture.filterMode = FilterMode.Bilinear;
                _connectionTexture.wrapMode = TextureWrapMode.Clamp;
                
                // Create a simple white gradient with transparency on edges
                _connectionTexture.SetPixel(0, 0, new Color(1, 1, 1, 0.2f));
                _connectionTexture.SetPixel(0, 1, new Color(1, 1, 1, 1.0f));
                _connectionTexture.SetPixel(0, 2, new Color(1, 1, 1, 0.2f));
                _connectionTexture.Apply();
            }
            return _connectionTexture;
        }
        
        private Texture2D _connectionTexture;
        
        private void HandleNodeEvents()
        {
            Event e = Event.current;
            
            // Right click to cancel connection
            if (e.type == EventType.MouseDown && e.button == 1)
            {
                if (isConnecting)
                {
                    isConnecting = false;
                    e.Use();
                }
            }
            
            // 处理大小调整交互的开始
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // 检查是否点击了调整大小的手柄
                for (int i = 0; i < nodesToDisplay.Count; i++)
                {
                    BaseNode node = nodesToDisplay[i];
                    
                    // 计算调整大小手柄在屏幕上的绝对位置
                    Rect resizeHandleRect = new Rect(
                        node.WindowRect.x + node.WindowRect.width - resizeHandleSize,
                        node.WindowRect.y + node.WindowRect.height - resizeHandleSize,
                        resizeHandleSize,
                        resizeHandleSize);
                        
                    if (resizeHandleRect.Contains(e.mousePosition))
                    {
                        isResizing = true;
                        resizingNode = node;
                        resizeStartPosition = e.mousePosition;
                        originalRect = node.WindowRect;
                        e.Use();
                        Debug.Log("开始调整大小: " + node.Name);
                        break;
                    }
                }
            }
        }
        
        private void ProcessSelectedNode()
        {
            if (selectedNode != null)
            {
                outputTexture = selectedNode.ProcessTexture(new Dictionary<string, BaseNode>());
                Repaint();
            }
        }
        
        private void SaveTextureToFile(Texture2D texture)
        {
            string path = EditorUtility.SaveFilePanel("Save Image", "", "output.png", "png");
            if (!string.IsNullOrEmpty(path))
            {
                byte[] bytes = texture.EncodeToPNG();
                File.WriteAllBytes(path, bytes);
                AssetDatabase.Refresh();
            }
        }
        
        // 添加OnInspectorUpdate以确保Editor持续重绘，特别是在调整大小时
        private void OnInspectorUpdate()
        {
            if (isResizing)
            {
                Repaint();
            }
        }
        
        // 新增方法：处理全局事件，与节点无关的事件
        private void HandleGlobalEvents()
        {
            Event currentEvent = Event.current;
            
            // 鼠标抬起时，如果正在调整大小，结束调整
            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
            {
                if (isResizing)
                {
                    isResizing = false;
                    resizingNode = null;
                    currentEvent.Use();
                }
            }
        }
        
        // 新增方法：绘制大小调整时的预览
        private void DrawResizePreview()
        {
            // 绘制正在调整大小的视觉效果
            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
            {
                Vector2 delta = currentEvent.mousePosition - resizeStartPosition;
                
                // 计算新的大小
                float newWidth = Mathf.Max(150, originalRect.width + delta.x);
                float newHeight = Mathf.Max(150, originalRect.height + delta.y);
                
                // 更新节点大小
                resizingNode.WindowRect = new Rect(
                    originalRect.x,
                    originalRect.y,
                    newWidth,
                    newHeight
                );
                
                currentEvent.Use();
                Repaint();
            }
            
            // 绘制调整框的高亮效果
            Handles.BeginGUI();
            Handles.color = new Color(1, 0.92f, 0.016f, 0.5f);
            Vector3[] points = new Vector3[5];
            Rect r = resizingNode.WindowRect;
            points[0] = new Vector3(r.x, r.y);
            points[1] = new Vector3(r.x + r.width, r.y);
            points[2] = new Vector3(r.x + r.width, r.y + r.height);
            points[3] = new Vector3(r.x, r.y + r.height);
            points[4] = points[0];
            Handles.DrawAAPolyLine(2.0f, points);
            Handles.EndGUI();
        }
        
        // 获取端口位置的辅助方法
        private Vector2 GetPortPosition(BaseNode node, int portIndex, bool isOutput)
        {
            if (node is BlendNode blendNode)
            {
                if (isOutput)
                {
                    // BlendNode的输出端口 (portIndex应该是2)
                    float outputPortY = Mathf.Min(70, node.WindowRect.height * 0.3f);
                    return new Vector2(node.WindowRect.x + node.WindowRect.width, node.WindowRect.y + outputPortY);
                }
                else
                {
                    // BlendNode的输入端口 (portIndex 0 或 1)
                    if (portIndex == 0)
                    {
                        float port1Y = Mathf.Min(50, node.WindowRect.height * 0.2f);
                        return new Vector2(node.WindowRect.x, node.WindowRect.y + port1Y);
                    }
                    else // portIndex == 1
                    {
                        float port2Y = Mathf.Min(90, node.WindowRect.height * 0.4f);
                        return new Vector2(node.WindowRect.x, node.WindowRect.y + port2Y);
                    }
                }
            }
            else
            {
                // 其他节点使用默认端口位置
                float portPositionY = Mathf.Min(70, node.WindowRect.height * 0.3f);
                if (isOutput)
                {
                    return new Vector2(node.WindowRect.x + node.WindowRect.width, node.WindowRect.y + portPositionY);
                }
                else
                {
                    return new Vector2(node.WindowRect.x, node.WindowRect.y + portPositionY);
                }
            }
        }
    }
} 