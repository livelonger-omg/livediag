/**
 * 目标（To-Be）服务蓝图种子数据
 * 来源：docs/service-blueprint.html（As-Is v2），按 F1–F15 逐条推导目标态。
 * 说明：asIs 为现状描述，toBe 为引入 LiveDiag 后的目标态；两者并存，便于做对照视图。
 */

export const SCHEMA_VERSION = 1

export const meta = {
  schemaVersion: SCHEMA_VERSION,
  title: '家宽直播上行质量投诉处理 · 目标服务蓝图',
  subtitle: '中国移动湖南公司有线中心 · LiveDiag 立项支撑材料（二）',
  version: 'To-Be v1',
  asOf: '2026-11-27',
  scope: '仅覆盖家宽场景，不含政企专线 / 商宽',
  sla: { accept: 10, arrive: 60, close: 360 },
  origin: '由 As-Is v2 推导（docs/service-blueprint.html）'
}

export const phases = [
  { id: 'p1', no: '阶段 01', name: '投诉受理', sla: 'SLA T+10min 建单', owner: '10086 坐席 / 网格经理', goal: '一次问清、自动打标、按直播类单独定级' },
  { id: 'p2', no: '阶段 02', name: '远程初判', sla: 'SLA T+1h 内上门', owner: '属地装维', goal: '上门前完成预检，减少无效上门' },
  { id: 'p3', no: '阶段 03', name: '开播复现', sla: '黄金档 19–23 点', owner: '属地装维 + 客户', goal: '零操作采集，无人值守也能留痕' },
  { id: 'p4', no: '阶段 04', name: '定界定责', sla: '2h ～ 2d', owner: '属地装维', goal: '分段证据定界，报告作为共同事实源' },
  { id: 'p5', no: '阶段 05', name: '处置验证', sla: '1 ～ 4h', owner: '属地装维', goal: '先定界后处置，前后同口径对比' },
  { id: 'p6', no: '阶段 06', name: '回访归档', sla: 'SLA T+6h 结单', owner: '客服 + 属地装维', goal: '报告即档案，经验可沉淀可复用' }
]

export const layers = [
  { id: 'evidence', name: '物理证据', en: 'Physical Evidence', tone: 'evi' },
  { id: 'customer', name: '客户行为', en: 'Customer Actions', tone: 'peo' },
  { id: 'front', name: '前台（可见）', en: 'Frontstage', tone: 'std' },
  { id: 'back', name: '后台（不可见）', en: 'Backstage', tone: 'std' },
  { id: 'support', name: '支撑流程与系统', en: 'Support Processes', tone: 'sys' }
]

/** 分界线插在指定层之后 */
export const dividers = [
  { after: 'customer', name: '互动线', en: 'Line of Interaction', desc: '客户与前台直接接触的边界' },
  { after: 'front', name: '可见性线', en: 'Line of Visibility', desc: '客户看不到的后台工作从这里开始' },
  { after: 'back', name: '内部互动线', en: 'Line of Internal Interaction', desc: '后台与支撑系统、跨专业协同的边界' }
]

/** cells: key = `${phaseId}.${layerId}` */
export const cells = {
  // ---------- 阶段 01 投诉受理 ----------
  'p1.evidence': {
    asIs: ['10086 工单号 / 短信', '微信聊天与语音记录', '上门预约通知'],
    toBe: ['工单自动带“直播类”标签与 SLA 倒计时', '受理短信附带诊断任务入口', '会话记录作为附件随单流转']
  },
  'p1.customer': {
    asIs: ['报障、描述“卡 / 糊 / 断”', '提供宽带账号与设备信息', '等待回电（W1）'],
    toBe: ['报障时只需回答 3 个直播场景问题', '无需描述细节与提供设备信息', '等待回电 ≤10min，进度可查'],
    f: [],
    w: ['W1']
  },
  'p1.front': {
    asIs: ['10086 坐席受理、建单、定级', '网格经理首次联系客户', '人工判断是否“直播类”（F1）'],
    toBe: ['坐席按“直播类”模板建单，平台 / 时段 / 码率结构化带入', '系统按归属自动派单并提示 1h 上门时限', '无需人工判断是否直播类'],
    f: ['F1']
  },
  'p1.back': {
    asIs: ['工单派发到属地班组（W2）', '宽带账号 / 在网状态核验', '投诉分类与统计口径（F2）'],
    toBe: ['工单自动派给属地装维，同步下发诊断任务', '账号 / ONU / 光功率预检结果随单下发', '按标签自动入统计口径，支持直播类单独出数'],
    f: ['F2'],
    w: ['W2']
  },
  'p1.support': {
    asIs: ['10086 客服系统 / CRM', '工单系统（派单、时限）', '宽带账号 / AAA 认证', '装维 App'],
    toBe: ['工单系统增加直播标签与结构化字段', 'LiveDiag 任务下发接口（建单即触发）', '预检结果自动回填工单']
  },

  // ---------- 阶段 02 远程初判 ----------
  'p2.evidence': {
    asIs: ['测速截图（多为下行）', '光猫指示灯照片', 'ping 结果截图'],
    toBe: ['上门前即拿到上行质量预检报告', '含 RTT / 抖动 / 丢包基线与质量分', '客户无需拍照、无需配合操作']
  },
  'p2.customer': {
    asIs: ['按指导重启光猫、路由器', '跑测速、拍照片', '配合度直接决定初判效果（F5）'],
    toBe: ['全程零操作', '装维到场即可看到数据', '不再被要求反复重启与测速'],
    f: ['F5']
  },
  'p2.front': {
    asIs: ['装维工程师电话指导', '解读测速结果（F3）', '是否上门的经验判断'],
    toBe: ['装维看预检报告决定上门或远程处置', '远程可闭环的直接远程结单', '上门判断有数据依据，不靠经验'],
    f: ['F3']
  },
  'p2.back': {
    asIs: ['网管查 PON 口、光功率', 'ONU 在线时长 / 认证状态', 'RMS 远程下发参数', '无法给出上行质量数据（F4）'],
    toBe: ['网管与上行质量看板并列：光功率 / ONU 状态 + 上行码率 / RTT / 丢包趋势', '预检结论自动标注“疑似接入网 / 平台 / 本机”', '必要时 RMS 先做远程参数修正'],
    f: ['F4']
  },
  'p2.support': {
    asIs: ['NMS / EMS（OLT、PON 口）', 'RMS 远程管理', '测速平台（下行主导）', '无“上行质量”采集能力'],
    toBe: ['LiveDiag 上行质量看板（5s 采样）', 'NMS / RMS 联动查询', '上行指标进入工单预检字段']
  },

  // ---------- 阶段 03 开播复现 ----------
  'p3.evidence': {
    asIs: ['直播伴侣码率 / 丢帧面板', '客户录屏（偶有）', '工程师手写时间点记录'],
    toBe: ['会话记录自动覆盖整场直播', '断流 / 掉帧时刻自动打点', '无需客户录屏与手写记录']
  },
  'p3.customer': {
    asIs: ['等待晚间黄金档才能复现（W3）', '约定时间开播配合观察', '黄金档（19–23 点）不能停播', '等待工程师到位（W4）'],
    toBe: ['客户照常开播，无需配合观察', '停顿 / 掉线时刻自动被记录', '工程师不必守点，事后统一看报告'],
    w: ['W3', 'W4']
  },
  'p3.front': {
    asIs: ['远程连线 / 上门观察', '手工记录卡顿时刻', '现场无法持续采集（F6）'],
    toBe: ['装维不再守点，事后调取会话时间线', '卡顿时刻与网络指标一一对应', '现场只需确认设备与线路状态'],
    f: ['F6']
  },
  'p3.back': {
    asIs: ['值班支撑在线协同（微信群）', '排班与客户开播时段错配（F7）', '临时部署采集工具受限（F8）'],
    toBe: ['零操作采集：免安装单文件、开机自启托盘常驻', '自动识别推流进程（TCP 443 / 1935、IPv6、UDP）', '无人值守记录，漏采率可测'],
    f: ['F7', 'F8']
  },
  'p3.support': {
    asIs: ['抖音直播伴侣（客户侧）', '远程桌面 / 向日葵', '命令行 ping / tracert', '微信（事实上的一线工单）'],
    toBe: ['LiveDiag 客户端常驻托盘，自动识别开播', '会话自动开始 / 结束', '采集元数据自动上传，无需人工导出']
  },

  // ---------- 阶段 04 定界定责 ----------
  'p4.evidence': {
    asIs: ['tracert / ping 截图', '网管光功率截图', '微信群里口头结论', '通常没有正式报告'],
    toBe: ['HTML 诊断报告：结论 + 曲线 + 连接快照', 'CSV 原始数据随附，可复核', '报告可外发，作为跨专业共同事实源']
  },
  'p4.customer': {
    asIs: ['等待结论', '反复被要求“再试一次”', '开始质疑网络质量（W5）'],
    toBe: ['当场拿到分段结论与报告', '清楚知道问题在哪一段、由谁处理', '不再被要求“再试一次”'],
    w: ['W5']
  },
  'p4.front': {
    asIs: ['属地装维给出“是哪一段的问题”结论', '凭经验 + 零散截图（F9）', '面对质疑缺少证据（F11）'],
    toBe: ['报告直接给出“接入网 / 平台 / 本机”结论', '装维照读结论，口径统一', '客户质疑时当场出示曲线与快照'],
    f: ['F9', 'F11']
  },
  'p4.back': {
    asIs: ['属地装维自行判定责任段', '微信群找有线中心 / 平台支撑', '协同结论只有口头，无工单留痕（F10）', '发起线路测试或二次派单'],
    toBe: ['五级分段定位自动逐跳定位并持续质检', '归因结论进入报告与工单附件', '微信群只做通知，结论以报告为准'],
    f: ['F10']
  },
  'p4.support': {
    asIs: ['网管系统', '拨测 / 线路测试系统', '微信群（跨专业协同的事实通道）', '缺分段质量数据源'],
    toBe: ['五级分段引擎 + 自动归因规则库', '报告作为跨专业共同事实源', '归因结果回填工单，形成责任依据']
  },

  // ---------- 阶段 05 处置验证 ----------
  'p5.evidence': {
    asIs: ['更换线缆 / 设备照片', '处置前后对比（凭体感）', '工单处理记录'],
    toBe: ['处置前后同会话曲线对比', '质量分变化量化呈现', '处置动作与指标绑定记录']
  },
  'p5.customer': {
    asIs: ['配合换线、改信道、重启', '再次开播验证', '不确定是否真的修好'],
    toBe: ['客户当晚照常开播', '次日报告自动复验效果', '客户能看到“修好了”的证据']
  },
  'p5.front': {
    asIs: ['实施常规处置动作', '复测体感（F13）', '判断能否结单'],
    toBe: ['按报告指向处置：换线 / 改信道 / 调 QoS', '处置后立即看同会话曲线', '对比达标即结单，不靠体感'],
    f: ['F13']
  },
  'p5.back': {
    asIs: ['线路故障单 / 光路调整', '更换端口、调整 QoS', '缺处置前基线（F12）'],
    toBe: ['处置前基线已由会话记录留存', '处置动作与前后指标一并归档', '同类处置沉淀为可复用方案'],
    f: ['F12']
  },
  'p5.support': {
    asIs: ['装维工单 App', 'RMS 参数下发', '光路 / 端口资源系统', '直播伴侣（验证用）'],
    toBe: ['LiveDiag 同会话前后对比', '处置动作登记（App 内勾选）', '资源系统变更与诊断报告关联']
  },

  // ---------- 阶段 06 回访归档 ----------
  'p6.evidence': {
    asIs: ['回访通话记录', '工单结单结论', '少量照片归档'],
    toBe: ['结单附诊断报告（可点开）', '回访时直接引用客观结论', '客户可自查当晚质量']
  },
  'p6.customer': {
    asIs: ['接听回访电话', '给出体感评价', '等待最终结单结论（W6）'],
    toBe: ['回访附报告链接', '评价基于可见数据而非体感', '结单进度透明可查'],
    w: ['W6']
  },
  'p6.front': {
    asIs: ['客服回访满意度', '填写结单结论（F14）'],
    toBe: ['回访话术带客观结论与质量分', '结单理由有数据支撑，可追溯', '需要时一键补发报告'],
    f: ['F14']
  },
  'p6.back': {
    asIs: ['工单归档、质量抽检', '经验停留在个人（F15）', '无知识库沉淀'],
    toBe: ['会话索引 + 案例标签，按首要原因聚类', '月度质量分析自动导出', '报告模板化，沉淀为案例库'],
    f: ['F15']
  },
  'p6.support': {
    asIs: ['工单系统（结单）', '客服回访系统', '知识库（未真正使用）', '无会话级数据归档'],
    toBe: ['原始数据留存 30 天 / 报告 180 天', '工单、报告、回访记录三向关联', '案例库与知识库打通']
  }
}

/** 失效点：现状 → 目标态 */
export const failPoints = [
  {
    id: 'F1', phase: 'p1', name: '直播类投诉无专用标签与分级',
    consequence: '无法统计、无法优先派单', cause: '工单分类体系未覆盖直播场景',
    auto: '中', status: 'building',
    solution: '工单系统增加“直播类”标签与结构化字段（平台 / 时段 / 码率 / 设备）',
    acceptance: '直播类工单可按标签检索，月度可单独出量与出率',
    owner: '工单系统 + 客服侧'
  },
  {
    id: 'F2', phase: 'p1', name: '故障描述非结构化',
    consequence: '关键信息（时段 / 平台 / 码率）缺失', cause: '无标准化采集模板',
    auto: '中', status: 'done',
    solution: '以会话自动记录替代人工问询，受理环节只做 3 项结构化勾选',
    acceptance: '工单必填字段完整率 ≥95%',
    owner: 'LiveDiag'
  },
  {
    id: 'F3', phase: 'p2', name: '测速以下行为主，掩盖上行瓶颈',
    consequence: '误判“网络正常”', cause: '测速平台指标偏下行',
    auto: '高', status: 'done',
    solution: '上行码率 / 上行丢包实时看板，作为初判第一屏',
    acceptance: '初判页面默认展示上行三项指标，采样 5s',
    owner: 'LiveDiag'
  },
  {
    id: 'F4', phase: 'p2', name: '只有“通 / 不通”和光功率，无质量分段',
    consequence: '无法提前排除接入网问题', cause: '缺分段 RTT / 丢包采集',
    auto: '高', status: 'done',
    solution: '网关 / 接入跳 / 推流节点三段并列监测',
    acceptance: '三段指标同时可见，异常段自动高亮',
    owner: 'LiveDiag'
  },
  {
    id: 'F5', phase: 'p2', name: '远程指导依赖客户配合度',
    consequence: '初判失败率高、反复沟通', cause: '操作门槛在客户侧',
    auto: '高', status: 'done',
    solution: '零操作采集（常驻托盘自动识别开播）',
    acceptance: '客户零操作即可产出完整会话记录',
    owner: 'LiveDiag'
  },
  {
    id: 'F6', phase: 'p3', name: '故障瞬时发生，无法回溯',
    consequence: '抓不到现场，约测反复', cause: '无持续采集与留痕',
    auto: '高', status: 'done',
    solution: '会话自动开始 / 结束 + 断流事件自动打点',
    acceptance: '每次断流均有时间戳与前后 30s 指标',
    owner: 'LiveDiag'
  },
  {
    id: 'F7', phase: 'p3', name: '客户开播时段与工程师班次错配',
    consequence: '复现跨天、投诉升级', cause: '依赖人工守点',
    auto: '高', status: 'done',
    solution: '无人值守记录，事后看报告',
    acceptance: '跨天复现占比从 ≈55% 降到 ≤15%',
    owner: 'LiveDiag'
  },
  {
    id: 'F8', phase: 'p3', name: '临时部署采集工具受限（客户机器 / 权限）',
    consequence: '现场无法取证', cause: '工具需安装与配置',
    auto: '高', status: 'done',
    solution: '免安装单文件 + 开机自启常驻',
    acceptance: '装维到场 5 分钟内完成部署并开始采集',
    owner: 'LiveDiag'
  },
  {
    id: 'F9', phase: 'p4', name: '缺分段质量证据，定界靠经验',
    consequence: '一次定界成功率低', cause: '无“本机→网关→接入网→节点”证据链',
    auto: '高', status: 'done',
    solution: '五级分段定位 + 自动归因结论',
    acceptance: '报告能明确回答“问题在哪一段”，一次定界成功率 ≥85%',
    owner: 'LiveDiag'
  },
  {
    id: 'F10', phase: 'p4', name: '跨专业协同走微信群，无工单留痕',
    consequence: '责任认定无据、协同耗时长', cause: '无共同事实源，结论只留在聊天记录里',
    auto: '中', status: 'building',
    solution: '统一报告作为共同事实源，可外发；协同结论回填工单',
    acceptance: '跨专业争议时以报告结论为准，微信群协同耗时 ≤1h',
    owner: 'LiveDiag + 工单系统'
  },
  {
    id: 'F11', phase: 'p4', name: '缺可提交证据，平台不认',
    consequence: '责任回归用户 / 属地', cause: '无规范报告与原始数据',
    auto: '高', status: 'done',
    solution: 'HTML 报告 + CSV 原始数据 + 连接快照',
    acceptance: '平台侧接受报告作为责任划分依据，争议率 ≤5%',
    owner: 'LiveDiag'
  },
  {
    id: 'F12', phase: 'p5', name: '处置前后无同口径对比',
    consequence: '无法证明修好，二次返工', cause: '缺处置前基线',
    auto: '高', status: 'done',
    solution: '会话内连续记录，处置前后曲线可比',
    acceptance: '同一会话可叠加处置前后曲线与质量分',
    owner: 'LiveDiag'
  },
  {
    id: 'F13', phase: 'p5', name: '试错式处置',
    consequence: '耗时、客户体验差', cause: '无定位即先换件',
    auto: '中', status: 'building',
    solution: '先定界后处置：工单增加“处置依据”必填项',
    acceptance: '处置动作与报告结论一致率 ≥90%',
    owner: '作业规范 + LiveDiag'
  },
  {
    id: 'F14', phase: 'p6', name: '结单无客观数据',
    consequence: '无法复盘与考核', cause: '归档仅文字结论',
    auto: '高', status: 'done',
    solution: '报告即档案（质量分 / 首要原因 / 曲线）',
    acceptance: '结单附件含报告，可检索可批量导出',
    owner: 'LiveDiag'
  },
  {
    id: 'F15', phase: 'p6', name: '经验不沉淀',
    consequence: '同类问题重复排查', cause: '缺案例库与模板',
    auto: '中', status: 'building',
    solution: '报告模板化 + 按首要原因聚类形成案例库',
    acceptance: '同类案例可一键调取，复盘覆盖率 ≥90%',
    owner: 'LiveDiag + 知识库'
  }
]

/** 等待点（现状保留，目标态逐条压缩） */
export const waits = [
  { id: 'W1', phase: 'p1', desc: '客户等待首次回电', toBe: '≤10min 内回电，进度可查' },
  { id: 'W2', phase: 'p1', desc: '工单派发到属地班组的等待', toBe: '自动派单，秒级下发' },
  { id: 'W3', phase: 'p3', desc: '等待晚间黄金档才能复现', toBe: '常驻采集，无需等档期' },
  { id: 'W4', phase: 'p3', desc: '等待工程师到位', toBe: '工程师不必守点' },
  { id: 'W5', phase: 'p4', desc: '等待定界结论', toBe: '报告当场出结论' },
  { id: 'W6', phase: 'p6', desc: '等待最终结单结论', toBe: '6h 内结单，进度透明' }
]

/** 指标：asIs 现状值，target 目标值；confirmed=true 表示口径已确认 */
export const metrics = [
  { id: 'K1', phase: 'p1', name: '直播类标识率', asIs: '<5%', target: '≥90%', unit: '%', confirmed: true, definition: '带“直播类”标签的工单数 ÷ 直播类投诉总数', kind: '核心' },
  { id: 'K2', phase: 'p1', name: '建单及时率', asIs: '≈95%', target: '100%', unit: '%', confirmed: true, definition: 'T+10min 内完成建单的工单占比（现行 SLA）', kind: 'SLA' },
  { id: 'K3', phase: 'p2', name: '上门及时率', asIs: '≈90%', target: '≥98%', unit: '%', confirmed: true, definition: 'T+1h 内到达现场的工单占比（现行 SLA）', kind: 'SLA' },
  { id: 'K4', phase: 'p2', name: '远程解决率', asIs: '≈20%', target: '≥35%', unit: '%', confirmed: false, definition: '远程闭环（未上门）工单数 ÷ 直播类工单总数', kind: '效率' },
  { id: 'K5', phase: 'p2', name: '无效上门率', asIs: '≈25%', target: '≤10%', unit: '%', confirmed: false, definition: '上门后未发现网络侧问题的工单 ÷ 上门工单总数', kind: '效率' },
  { id: 'K6', phase: 'p3', name: '复现成功率', asIs: '≈40%', target: '≥85%', unit: '%', confirmed: false, definition: '成功记录到故障时段会话的工单 ÷ 需复现工单', kind: '核心' },
  { id: 'K7', phase: 'p3', name: '跨天复现占比', asIs: '≈55%', target: '≤15%', unit: '%', confirmed: false, definition: '复现跨自然日的工单 ÷ 需复现工单', kind: '效率' },
  { id: 'K8', phase: 'p3', name: '数据漏采率', asIs: '未统计', target: '≤5%', unit: '%', confirmed: false, definition: '应有会话记录但缺失的工单 ÷ 应采集工单', kind: '质量' },
  { id: 'K9', phase: 'p4', name: '一次定界成功率', asIs: '≈45%', target: '≥85%', unit: '%', confirmed: false, definition: '首次结论被后续验证为正确的工单 ÷ 定界工单', kind: '核心' },
  { id: 'K10', phase: 'p4', name: '跨专业协同耗时', asIs: '≈6h', target: '≤1h', unit: 'h', confirmed: false, definition: '发起到拿到跨专业确认结论的平均时长', kind: '效率' },
  { id: 'K11', phase: 'p4', name: '责任争议率', asIs: '≈30%', target: '≤5%', unit: '%', confirmed: false, definition: '发生责任归属争议的工单 ÷ 需定责工单', kind: '质量' },
  { id: 'K12', phase: 'p5', name: '一次修复率', asIs: '≈55%', target: '≥85%', unit: '%', confirmed: false, definition: '一次处置后 7 天内未再报障的工单占比', kind: '核心' },
  { id: 'K13', phase: 'p5', name: '二次返工率', asIs: '≈30%', target: '≤10%', unit: '%', confirmed: false, definition: '同一故障 7 天内二次上门的工单 ÷ 上门工单', kind: '质量' },
  { id: 'K14', phase: 'p6', name: '一次解决率', asIs: '待统计', target: '≥60%', unit: '%', confirmed: true, definition: '投诉一次上门解决数 ÷ 投诉总数；“一次上门解决”指当日一次上门解决并完成工单归档', kind: '核心' },
  { id: 'K15', phase: 'p6', name: '重复投诉率', asIs: '待统计', target: '≤8%', unit: '%', confirmed: true, definition: '近 30 天投诉量 >1 次的客户数 ÷ 近 30 天投诉客户数', kind: 'SLA' },
  { id: 'K16', phase: 'p6', name: '结单及时率', asIs: '≈85%', target: '≥95%', unit: '%', confirmed: true, definition: 'T+6h 内完成结单的工单占比（现行 SLA）', kind: 'SLA' }
]

/** 服务质量差距（Zeithaml） */
export const gaps = [
  {
    id: 'G1', name: 'G1 认知差距', definition: '管理者对客户期望理解不足',
    asIs: '把直播投诉当普通上网故障处理，未识别“上行连续性”是核心诉求',
    evidence: '工单无直播标签（F1 / F2）',
    action: '以旅程地图 + 会话数据量化“卡顿 = 上行”的真实占比，作为立项依据',
    target: '直播类投诉可识别、可统计、可单独定级'
  },
  {
    id: 'G2', name: 'G2 标准差距', definition: '服务标准与客户期望不匹配',
    asIs: '现行 SLA 只考核 10min / 1h / 6h 时限，无上行质量阈值；判定只看光功率与“通不通”；6h 结单与黄金档复现互斥',
    evidence: '指标体系缺上行 RTT / 丢包口径；时限结构不适配直播场景',
    action: '定义质量分与分段阈值（网关 / 接入跳 / 节点）并纳入 SLA；直播类另设“次日复验”，不以当日结单论成败',
    target: '形成上行质量阈值 + 直播类专用时限口径'
  },
  {
    id: 'G3', name: 'G3 交付差距', definition: '标准执行不到位',
    asIs: '依赖个人经验，同一故障不同人结论不同',
    evidence: 'F9 / F13',
    action: '工具给统一口径：自动采集 + 自动归因，减少人工判断',
    target: '同一会话不同人看同一结论'
  },
  {
    id: 'G4', name: 'G4 沟通差距', definition: '对外承诺与实际交付不一致',
    asIs: '“网络没问题”无证据支撑，客户不信',
    evidence: 'F11',
    action: '可外发报告，用客户看得懂的曲线说明',
    target: '客户当场认可结论，不重复质疑'
  },
  {
    id: 'G5', name: 'G5 感知差距', definition: '客户感知与服务质量落差',
    asIs: '问题“解决”后客户仍不敢信，次日晚间复发即再次投诉',
    evidence: '重复投诉率（同一客户 30 天内 >1 次）、二次返工率 ≈30%',
    action: '处置前后对比 + 回访附报告，形成闭环感知',
    target: '重复投诉率 ≤8%、满意度 ≥95%'
  }
]

/** 建设内容映射：可直接进立项文档 */
export const buildItems = [
  {
    id: 'B1', phase: 'p2', fails: ['F3', 'F4'],
    content: '上行质量看板：推流 / 整机上行、网关与接入跳 RTT·抖动·丢包、推流节点 RTT·丢包',
    acceptance: '开机即显示，采样 5s 粒度，指标可回溯', status: 'done'
  },
  {
    id: 'B2', phase: 'p3', fails: ['F5', 'F6', 'F7', 'F8'],
    content: '零操作采集：免安装单文件、开机自启托盘常驻、自动识别推流进程（TCP 443 / 1935、IPv6、UDP）、会话自动开始与结束',
    acceptance: '客户无需操作即可产出完整会话记录；漏采率可测', status: 'done'
  },
  {
    id: 'B3', phase: 'p4', fails: ['F9', 'F10', 'F11'],
    content: '五级分段定位与自动归因：自动逐跳定位“网关之后第一跳”并持续质检；异常自动生成“接入网 / 平台 / 本机”结论；HTML 报告 + CSV + 连接快照',
    acceptance: '报告能回答“问题在哪一段”，且可外发', status: 'done'
  },
  {
    id: 'B4', phase: 'p5', fails: ['F12', 'F13'],
    content: '同会话内处置前后对比（曲线 + 质量分），断流与恢复自动打点',
    acceptance: '处置效果可量化，不依赖体感', status: 'done'
  },
  {
    id: 'B5', phase: 'p6', fails: ['F14', 'F15'],
    content: '会话索引 + 报告长期留存（原始 30 天 / 报告 180 天），模板化便于沉淀案例',
    acceptance: '可检索、可批量导出、可做月度分析', status: 'done'
  },
  {
    id: 'B6', phase: 'p1', fails: ['F1', 'F2'],
    content: '工单标签与结构化字段（平台 / 时段 / 码率 / 设备）',
    acceptance: '直播类工单可统计、可分流', status: 'planned'
  },
  {
    id: 'B7', phase: 'p1', fails: ['F10'],
    content: '与工单 / 网管系统对接：结果回填、报告随单、数据回流分析',
    acceptance: '诊断结果进入工单闭环，形成数据资产', status: 'building'
  },
  {
    id: 'B8', phase: 'p6', fails: ['F15'],
    content: '案例库与复盘看板：按首要原因聚类，支持一键调取相似案例',
    acceptance: '复盘覆盖率 ≥90%，同类问题平均排查时长下降 50%', status: 'planned'
  }
]

export const seed = {
  meta, phases, layers, dividers, cells, failPoints, waits, metrics, gaps, buildItems,
  ui: { viewMode: 'compare', activeTab: 'board' }
}
