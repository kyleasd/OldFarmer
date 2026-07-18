# OldFarmer 项目记忆

## 模组信息
- 名称: OldFarmer (爷爷回来了) | UniqueID: `Kyle.OldFarmer`
- 核心文件: ModEntry.cs, GrandpaSpiritOrbiter.cs, TillingModule.cs, WoodcuttingModule.cs, WateringModule.cs, ScythingModule.cs, PlantingModule.cs, CombatModule.cs, SharedTargetManager.cs, SanManager.cs, SanBarDrawer.cs
- 战斗相关: GrandpaFighterBehavior.cs, MonsterScanner.cs

## 架构
- **模块化**: 每个行为一个 `*Module` 类（Tilling/Woodcutting/Watering/Scything/Planting/Combat），对外接口 `Enable()`/`Disable()`/`IsEnabled`/`Update()`/`Draw()`。`ModEntry` 持有所有模块，默认不激活。
- **SharedTargetManager**: 防多爷爷抢同一目标。Module 持有 `SharedTargetManager`，通过 `SetTargetManager` 传给 Behavior。已应用于 Tilling/Watering/Scything/Planting/Woodcutting。
- **多爷爷**: `ModEntry._grandpas` 是 `List<GrandpaSpiritOrbiter>`，上限 10 个，按 `count * 2π/10` 轨道分布。所有 Module 用 `List<(behavior, orbiter)>`。SAN 共享。
- **战斗优先级最高**: 怪物在附近时 ModEntry 禁用其他模块。

## 战斗模块关键设计
- FSM: Orbiting → MovingToTarget → Attacking → Cooldown → NextTarget
- 扫描半径 10 格(640px)，牵引半径 16 格(1024px)，攻击范围 = 命中范围 = 256px(4格)
- **伤害**: `10 + 10 × CombatLevel`，AoE 128×128 居中于怪物
- **SAN**: 命中 -0.5，击杀额外 -2

### `damageMonster` 检查链（SDV 1.6 反编译确认）
`GameLocation.damageMonster(area, minDmg, maxDmg, isBomb, knockBack, precision, crit, critMult, triggerInvincibleTimer, who)` 内部对每个 character：
1. `TakesDamageFromHitbox(area)` — bounding box 交集
2. `!monster.IsInvisible` — 不可见检查（isBomb **不绕过**）
3. **`!monster.isInvincible()`** — 无敌帧检查（isBomb **不绕过**！旧文档说绕过是错误的）
4. `(isBomb || isProjectile || isMonsterDamageApplicable(who, monster) || isMonsterDamageApplicable(who, monster, false))` — isBomb/isProjectile=true 绕过视线(LOS)检查
5. `hitWithTool(who.CurrentTool)` — 返回 true 则整个方法 return false（isBomb 不绕过，但仅特定怪物+工具组合如 Rock Crab + 镐子）

**关键修复（2026-07-18）**: 每次 `damageMonster` 调用前 `monster.invincibleCountdown = 0` 清除无敌帧。原因：玩家攻击设置的无敌帧会阻止爷爷的伤害（即使 isBomb=true），且无敌帧持续 ~225ms > 爷爷攻击间隔 ~167ms，导致大部分伤害被静默跳过。

### Mummy 击杀（两连击，同一帧）
- 两击均用 `isBomb=true`（绕过 isMonsterDamageApplicable，且 HP=0 时 Crusader 检查需 `!isBomb` 所以始终走倒地路径）
- Beat 1: `reviveTimer<=0` 时 `isBomb=true` 伤害 → HP 归零 → 倒地(HP=Max, reviveTimer=10000)
- Beat 2: `reviveTimer>0 && Health>0` 时 `isBomb=true` → Health=0 → 死亡
- 两击前均清 `invincibleCountdown = 0`

### Armored Bug（装甲甲虫）击杀
- **根因**: `Bug.takeDamage` 检查 `isArmoredBug && (isBomb || !(CurrentTool is MeleeWeapon) || !hasBugKiller)` → return 0。**isBomb=true 反而让装甲甲虫免疫！**（旧注释 `isArmoredBug && !isBomb` 是错的）
- **修复**: 装甲甲虫走专用路径 `DealDamageToArmoredBug`：临时给玩家塞一把缓存好的带 BugKiller 附魔的 MeleeWeapon（`(W)0` 训练剑 + `AddEnchantment(new BugKillerEnchantment())`），用 `isBomb=false` + `isProjectile=true`（绕过 LOS 视线检查）调 `damageMonster`，调用后立即还原 `player.CurrentTool`
- BugKiller 的 `OnCalculateDamage` 也需 `!fromBomb` 才触发双倍伤害，进一步确认必须 isBomb=false

## 功能时间线
- 2026-06-22: 模块化架构
- 2026-06-28: 过季种子气泡提示
- 2026-07-04: 战斗模块 / 神秘糖浆召唤（吃喝动画后召唤）/ 动态SAN上限(100+10×全技能和)
- 2026-07-08: 多爷爷召唤
- 2026-07-15: SharedTargetManager
- 2026-07-18: Mummy 击杀 + 战斗伤害修复（清无敌帧 / isBomb统一 / 牵引半径放宽 / HitRange=AttackRange）
- 2026-07-18: Armored Bug 0 伤害修复（isBomb=true 导致免疫 → 专用路径 isBomb=false + 临时 BugKiller 武器）

## Bug 修复记录
- 2026-06-19: 工具劫持 — 保存 `Items[0]` + `CurrentToolIndex` 而非 `CurrentItem`
- 2026-06-21: 出门回来瞬移 — 新增 `wasOnFarm` 标志位重置 WorldPosition
- 2026-06-26: 雨天耕地自动浇水 — `DoFunction` 后强制 `state.Value = HoeDirt.dry`
- 2026-07-18: 装甲甲虫伤害为0 — `isBomb=true` 触发 `Bug.takeDamage` 装甲免疫分支（条件含 isBomb），改用 `isBomb=false`+临时BugKiller武器+`isProjectile=true`

## SDV API 坑
- `Scythe` 类不存在，用 `MeleeWeapon.Name.Contains("Scythe")`
- `Monster.takeDamage` 返回 int: -1=未伤害, 正数=伤害值, 999=特殊击杀
- `Monster.Health` 可直接读写; `Mummy.reviveTimer` 是 `NetInt` 用 `.Value`
- `monster.invincibleCountdown` 是 public int，可直接赋值
- `Bug.isArmoredBug` 是 `NetBool`，用 `.Value`；装甲甲虫免疫条件含 `isBomb`（isBomb=true 会免疫！），只有 `isBomb=false` + 带 BugKiller 附魔的 MeleeWeapon 才能造成伤害
- `GameLocation.playSoundPitched` 不存在，用 `playSound(string)`
- `crop.harvest(int x, int y, HoeDirt, JunimoHarvester, bool)` — 五参数
- 干草: `ItemRegistry.Create("(O)178")`; 粮仓: `Farm.tryToAddHay(int)`
- `SpriteText`: 用 `drawStringHorizontallyCenteredAt()` / `getWidthOfString()` / `getHeightOfString()`（小写开头）
- `viewport.TitleSafeArea`（属性）替代 `GetTitleSafeArea()`
- `ResourceClump` 多格检测: 遍历 terrainFeatures 按 width/height 展开成 HashSet 查表
- `MeleeWeapon.AddEnchantment(...)`（PascalCase），`hasEnchantmentOfType<T>()`（小写开头）
- `damageMonster` 的 `isProjectile` 参数可绕过 `isMonsterDamageApplicable` 视线检查，无其他副作用

## 开发原则
查文档优先，反编译最后：SMAPI wiki → SMAPI 源码 → SDV 1.6 源码(GitHub) → `ilspycmd`
