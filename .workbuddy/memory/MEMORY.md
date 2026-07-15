# OldFarmer 项目记忆

## 项目概述

### 模组信息
- 名称: OldFarmer (爷爷回来了)
- UniqueID: `Kyle.OldFarmer`
- 描述: 爷爷精灵自动在农场耕地，状态机驱动（Orbiting → MovingToTarget → ChargingHoe → TillingTile → Cooldown → NextTarget）
- 核心文件: ModEntry.cs, GrandpaSpiritOrbiter.cs, TileScanner.cs, TillingExecutor.cs, GrandpaTillerBehavior.cs, TillingModule.cs, WoodcuttingModule.cs, GrandpaWoodcutterBehavior.cs, WoodcutterScanner.cs, WoodcutterExecutor.cs, ScythingModule.cs, GrandpaScytherBehavior.cs, ScythingScanner.cs, ScythingExecutor.cs, CombatModule.cs, GrandpaFighterBehavior.cs, MonsterScanner.cs

### 模块化架构（2026-06-22）
- 耕地功能已封装为 `TillingModule`（TillingModule.cs），组合 `GrandpaTillerBehavior` + `HighlightDrawer`。
- 砍树功能已封装为 `WoodcuttingModule`（WoodcuttingModule.cs），组合 `GrandpaWoodcutterBehavior` + 斧头渲染。
- 镰刀功能已封装为 `ScythingModule`（ScythingModule.cs），行为与耕地完全对齐：9×9 范围蓄力 + 一次性收割草/作物。草收割后调用 `Farm.tryToAddHay(1)` 存入粮仓（SDV 1.6 原生 API），粮仓满/不存在时 fallback 到 `ItemRegistry.Create("(O)178")` + `createItemDebris` 掉在地上。
- 对外接口：`Enable()` / `Disable()` / `IsEnabled` / `Update()` / `Draw()`。
- `ModEntry` 只持有 `TillingModule` + `WoodcuttingModule` + `WateringModule` + `ScythingModule` + `PlantingModule` + `CombatModule`，构造时默认均不激活。
- 后续其他行为（挖矿/钓鱼/采集）应仿照此模式各建一个 `*Module` 类。

### SharedTargetManager 通用认领机制（2026-07-15）
- 所有支持多爷爷的模块统一使用 `SharedTargetManager` 防止多个爷爷抢占同一个目标。
- **文件**: `SharedTargetManager.cs` — 封装 `TryClaim(Vector2)` / `Release` / `IsClaimed` / `Clear`。
- **用法模板**（模块 + Behavior，9×9 中心点模式）：
  1. Module: 声明 `private readonly SharedTargetManager _targetMgr = new()`
  2. Module: `AddGrandpa(orbiter)` 中 `behavior.SetTargetManager(_targetMgr)`
  3. Module: `RemoveAllGrandpas()`/`Disable()` 中 `_targetMgr.Clear()`
  4. Behavior: 添加 `SetTargetManager`/`ClaimCenter`/`ReleaseClaim`/`TryPickNextTarget` 方法
  5. Behavior: 移除 `centerQueue`，`TickOrbiting`/`TickNextTarget` 统一调用 `TryPickNextTarget`
  6. Behavior: `Reset()` 和 `TransitionTo(Orbiting)` 中调 `ReleaseClaim()`
- **已应用模块**: Tilling, Watering, Scything, Planting, Woodcutting
- **新模块开发时**：必须引用 `SharedTargetManager`，不要把 `HashSet<Vector2>` 写在模块里。

### 最新功能（2026-07-08）
- **多爷爷召唤（Multi-Grandpa Summoning）**：
  - 玩家可多次喝神秘糖浆召唤多个爷爷，上限 10 个，共用同一条 SAN 条。
  - `GrandpaSpiritOrbiter` 移除了 `static Instance` 单例模式，改为多实例。新增 `angleOffset` 构造参数，10 个爷爷按 `count * 2π/10` 在轨道均匀分布。
  - 所有 6 个 Module 从单 `(behavior, orbiter)` 改为 `List<(behavior, orbiter)>` 模式，新增 `AddGrandpa(orbiter)` / `RemoveAllGrandpas()` 方法。
  - `ModEntry` 持有 `List<GrandpaSpiritOrbiter> _grandpas`，`SummonGrandpa()` 每次创建新 orbiter 并注册到所有模块。
  - SAN 归零时所有 orbiter 同时淡出；全部销毁后清空列表和模块。
  - 模块 `Update()` 跳过 `IsFadingOut() || IsDestroyed` 的 orbiter。
  - 模块构造函数不再需要 orbiter 参数（PlantingModule 仍需 IMonitor）。

### 最新功能（2026-07-04）
- **战斗模块（CombatModule）**：
  - 玩家周围 10 格内有怪物时，爷爷自动变红并前往攻击。
  - 新增 `MonsterScanner`（扫描 `location.characters` 中的 `Monster`）、`GrandpaFighterBehavior`（FSM: Orbiting → MovingToTarget → Attacking → Cooldown → NextTarget）、`CombatModule`（标准模块包装）。
  - 战斗不限于农场，任何地方都能触发（与耕地/浇水等不同）。
  - `GrandpaSpiritOrbiter` 新增 `CombatWorldPosition`（最高渲染优先级）和 `IsInCombatMode`（红色着色 `new Color(255, 60, 60)`）。
  - `ModEntry` 中战斗优先级最高——怪物在附近时禁用所有其他模块。
  - 使用 `monster.takeDamage(40, xKnockback, yKnockback, false, 0, player)` 造成伤害（SDV 1.6 已验证签名）。
  - SAN 消耗：命中 -0.5，击杀额外 -2。
- **神秘糖浆召唤改造**：
  - 移除农场限制，任何地方都能召唤爷爷。
  - 召唤方式从"右键立即消耗"改为"喝下神秘糖浆后召唤"——不再 suppress 按键，让游戏自然播放吃喝动画。
  - 实现方式：`OnButtonPressed` 检测手持 Mystic Syrup + 动作键，设置 `_pendingSummon` 标志；`OnUpdateTicked` 中检测 `player.isEating` 从 true→false 的转换，此时才调用 `SummonGrandpa()`。
  - 玩家喝下糖浆后既获得原版 buff，又召唤爷爷。
- **动态 SAN 上限**：
  - `SanManager.MaxSan`（const 100）改为 `GetMaxSan(Farmer)` 方法：`100 + 10*(耕种+采矿+采集+钓鱼+战斗)`，最低 100。
  - 全技能 0 级 = 100 SAN，全技能 10 级 = 600 SAN。
  - `SanBarDrawer.Draw` 中 `maxSan` 改为 `SanManager.GetMaxSan(player)`，条形高度和数值显示自动适配。
  - SAN < 20 的抖动/脉冲阈值仍为绝对值（未改为相对比例）。

### 最新功能（2026-06-28）
- 过季种子气泡提示：玩家手持过季种子时，爷爷头上出现气泡，文字为"乖孙，这种子过季了哟"。
- 实现方式：`GrandpaSpiritOrbiter` 加入 `BubbleText` / `ShowBubble` 属性 + `_bubbleAlpha` 淡入淡出动画；
  `ModEntry` 每帧调用 `UpdateOutOfSeasonBubble()` 检测手持种子是否过季（`Crop.TryGetData` + `cropData.Seasons` + `location.GetSeason()`），设置气泡状态。
- 气泡绘制：黑底白框矩形 + `SpriteText.drawStringHorizontallyCenteredAt`。

### 已知 Bug 修复记录
- 2026-06-19: 修复工具劫持 Bug — `SetTemporaryTool` 将锄头放入 slot 0 但 `savedTool` 保存的是 `player.CurrentItem`（当前手持任意槽位物品），导致恢复时 slot 0 被错误覆盖。修改为保存 `player.Items[0]` 和 `CurrentToolIndex`。
- 2026-06-21: 修复出门回来爷爷瞬移 Bug — `WorldPosition` 跨场景保留旧值，玩家回到农场时 `IsTilling` 切换瞬间造成精灵瞬移。新增 `wasOnFarm` 标志位，每次进入农场第一帧将 `WorldPosition` 重置为玩家位置。
- 2026-06-26: 修复雨天耕地自动浇水 — 游戏原版 `GameLocation.makeHoeDirt` 在 `IsRainingHere()` 时直接创建 watered 状态的 HoeDirt（原版雨天耕地免浇水行为）。`TillingExecutor.TillArea` 在 `DoFunction` 后强制 `state.Value = HoeDirt.dry`，保证爷爷耕的地干燥需手动浇。`HoeDirt.tickUpdate` 每帧不重新浇水，所以一次强制干燥即可保持。

### 开发原则：查文档优先，反编译是最后手段
- **遇到 API 不确定，先查文档，不要第一时间想反编译！**
- 查阅顺序：
  1. SMAPI 官方文档：https://stardewvalleywiki.com/Modding:Index（wiki 的 Modding 系列文章，覆盖大部分 API 用法）
  2. SMAPI 源码 / 示例 mod：https://github.com/Pathoschild/SMAPI
  3. SDV 原版源码（1.6 已开源）：https://github.com/ConcernedApe/StardewValley
  4. 反编译（`ilspycmd`）作为最后手段，仅在文档和源码都找不到时才用

### 游戏内部行为参考（已通过文档/源码确认）
- `Hoe.DoFunction` 不浇水，只调用 `makeHoeDirt` 创建 HoeDirt。
- `GameLocation.makeHoeDirt`：`new HoeDirt((IsRainingHere() && isOutdoors) ? 1 : 0, this)`——雨天耕地直接产出浇水地。
- `HoeDirt.tickUpdate` 每帧不浇水，只处理摇晃动画；`dayUpdate` 才处理 paddy/肥料保水/晾干。
- `HoeDirt` 状态常量：`dry=0`, `watered=1`, `2`=paddy 特殊。
- `SpriteText` 正确方法名（SDV 1.6）：没有 `draw()`/`Draw()` 方法；绘制文本用 `drawStringHorizontallyCenteredAt()`；测量用 `getWidthOfString()` / `getHeightOfString()`。均小写开头。

### SDV API 坑记录（编译实战总结）
- `Scythe` 类不存在于 SDV API，镰刀检测用 `MeleeWeapon.Name.Contains("Scythe")`
- `Monster.takeDamage` 签名（SDV 1.6，已验证）：`takeDamage(int damage, int xTrajectory, int yTrajectory, bool isBomb, double addedPrecision, Farmer who)`，返回 bool（true=击杀）
- `Monster.Health` 属性（int，封装 NetInt）可直接读写
- `GameLocation.playSoundPitched` 不存在，用 `playSound(string)`
- `GameLocation.characters` 是 `List<NPC>`，遍历可获取怪物（`is Monster`）
- `crop.harvest` 签名（SDV 1.6）：`harvest(int x, int y, HoeDirt soil, JunimoHarvester junimo, bool isFromGoldPan)`（五参数）
- `Object` 构造不能传 `Vector2`；SDV 1.6 创建可拾取干草物品用 `ItemRegistry.Create("(O)178")`
- `Farmer.hasSilo()` / `Utility.tryToAddHayToSilo()` 不存在；SDV 1.6 直接用 `Farm.tryToAddHay(int number)`（返回 leftover count，0=全部存入）
- `Grass.performToolAction` 签名不稳定，避免直接调用，改为手动移除草 + `farm.tryToAddHay(1)`
- `viewport.TitleSafeArea`（属性）替代 `viewport.GetTitleSafeArea()`（MonoGame 扩展方法在本项目里报错）
- SAN 值 HUD 条（2026-06-27）：`SanManager.cs` 管理数据（modData key: `Kyle.OldFarmer/SAN`），`SanBarDrawer.cs` 负责绘制，注册 `RenderedHud` 事件，位置在体力条左侧 56px（血量条显示时再左 56px）
- `ResourceClump` 占用检测：巨大化作物等多格物体只在其原点 tile 有 `TerrainFeature`，非原点占用的格子不会出现在 `terrainFeatures` 里。正确做法：遍历 `location.terrainFeatures.Pairs`，对每个 `ResourceClump` 按其 `width.Value` / `height.Value` 展开所有占用格子，放入 `HashSet<Vector2>`，判断时查表 `Contains(tile)`。
- 性能要点：预建 Hash Set 一次（在扫描函数入口），传给每个格子的判断函数查表；不要在判断每个格子时都重建。
