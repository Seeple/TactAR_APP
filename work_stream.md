# AR Human in the loop

## 阶段一：轨迹AR渲染

### 任务描述

hack一段轨迹，在AR系统中通过点加坐标轴的方式可视化出来

### 工作进度

#### 环境配置

- 将项目导入Unity Editor
- 检查Project Settings 和 Package Manager
    - 安装了ARCore XR Plugin(用于安卓设备的AR插件)
    - 了解Oculus XR Plugin
        - 适配Meta系列
    - 检查Package - Asset Store
        - 出现报错：Error while getting auth code
        - 应该是网络问题，关了代理重新进去就好了
- 检查脚本需要的依赖项
    - [ ] Visualizer中BsonReader报了warning \
        升级版本后依然存在，怀疑是误报，在运行时检查
    - [x] 有些时候打开项目一段时间后，会显示Error acquring .Net(.csproj的依赖项)! \
    可能时因为没有安装.NET SDK

#### 熟悉Unity空间

- OVRCameraRig: VR体验的核心，管理所有追踪设备（HMD、控制器、手）的坐标空间和原始数据。

    - TrackingSpace: 追踪参考点，代表VR空间中的“地面”。重置视角时会调整此对象。

        - CenterEyeAnchor: 代表用户双眼中心的精确位置和旋转。常用于放置跟随视野的UI。

        - LeftEyeAnchor/RightEyeAnchor: 分别代表用户左/右眼的精确位置。主要用于特殊立体渲染特效。

            - [ ] LeftEye/RightEye/CenterAnchor: The associated script cannot be loaded，应该没有关系，因为这个摄像头用不到，但在实测的时候还是留意一下  

        - LeftHandAnchor/RightHandAnchor: 手部/控制器的根锚点，其姿态由Oculus运行时直接驱动。

            - LeftControllerAnchor: 代表物理控制器模型的锚点。3D控制器模型应作为其子物体。

            - LeftOVRHand: 手部骨骼模型的根节点，挂载OVRHand和OVRSkeleton脚本。

            - LeftControllerInHandAnchor: 用于视觉偏移校正，表示“控制器在虚拟手中应有的位置”。

                - LeftHandOnControllerAnchor: 用于视觉偏移校正，表示“虚拟手在控制器上应有的位置”。

        - LeftHandAnchorDetached: 备用锚点，用于在手部追踪丢失时平滑过渡或隐藏手部模型，避免突兀消失。

    - OVRInteraction (自定义架构): 高级交互系统的“大脑”，负责协调和管理复杂的交互逻辑。

        - OVRHmd: 基于头部（视线）的交互逻辑管理器，常用于实现凝视射线（Gaze Ray）交互。

        - OVRControllerDriveHands: 负责在用户使用控制器时，用手柄数据驱动虚拟手呈现“握持控制器”的预设姿势。

        - OVRHands: 包含完整交互逻辑的“实体手”。

- Pose : 模块化的手势识别系统，用于检测特定手势并驱动游戏逻辑。

    - RightThumbsUpPose: 右手“点赞”手势的检测单元。

        - Hand Ref: 引用管理器，负责获取并持有对右手OVRHand组件的引用。

        - Active State Selector: 手势检测与状态机，持续监测手势是否被做出，并输出IsActive状态或触发事件。

- Keyboard：挂载Mykeyboard.cs，管理虚拟键盘

- Calibration: 挂载Calibration.cs，管理calibration过程

- Main：挂载visualizerServer.cs，VRcontroller.cs，UnitymainThreadDispatcher.cs，负责VR和work station之间的通信

#### 可视化：一条简单的，带有三维箭头的轨迹

##### 将原始TactAR成功部署到Meta Quest 3上

##### 尝试配置VR Simulator，并使用VR simulator调试程序

安装的Package：

- XR Interaction Toolkit

    https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.0/manual/index.html

- XR Device Simulator

    https://docs.unity.cn/Packages/com.unity.xr.interaction.toolkit@2.3/manual/xr-device-simulator-overview.html



## 长期规划

### policy相关

policy实时可视化（policy expolration）

### 手势识别

用手拽动那个轨迹

## 需要学习的材料

### VR相关

学习Unity相关的编程（C#）

TactAR相关代码：尤其关注坐标轴校准和对齐的流程以及视频流

### policy相关

#### policy exploration

类似于RL中的policy exploration

#### Data aggregation

##### DexCap

##### Robot Learning on the Job



