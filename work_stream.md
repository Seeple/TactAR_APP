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

#### 项目结构梳理



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



