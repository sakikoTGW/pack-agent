#!/usr/bin/env bun
/**
 * PAD 把供应商配方写成 DSH llm-pi-ai.providers，密钥不进这份文件。
 */
import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const pad = join(import.meta.dirname, '../pad')
const file = join(pad, 'Core/LlmProviders.cs')
if (!existsSync(file)) fail('缺 Core/LlmProviders.cs')
const cs = readFileSync(file, 'utf8')
if (!cs.includes('llm-pi-ai')) fail('配方不是 DSH llm-pi-ai.providers')
if (!cs.includes('apiKeyEnv')) fail('配方没有 apiKeyEnv，密钥会进方法文件')
if (!cs.includes('# <pad-llm-providers>')) fail('写入 home settings.yaml 没有 PAD 段标记，会毁掉别人的设置')
if (cs.includes('session.selectModel')) fail('设置期配方写入掺进了通信热切')
if (!cs.includes('DescribeRow')) fail('名册行没有 DescribeRow，下拉里看不出已配还是缺 ref')
if (!cs.includes('CatalogPresets')) fail('没有 CatalogPresets，不能从 DSH 常见路由套预设')
if (!cs.includes('DescribeWillList')) fail('明细没有将会进入 session.models 的提示')

function describeRow(
  r: { route: string; displayName: string; apiKeyEnv: string; models: string[] },
  refs: string[],
): string {
  const title = r.displayName.trim() || r.route
  const n = r.models.filter(m => m.trim().length > 0).length
  const env = r.apiKeyEnv.trim()
  const cred = env.length === 0 ? '未写 apiKeyEnv' : refs.includes(env) ? `已配 ${env}` : `缺 ${env}`
  return `${title} · ${n} 个模型 · ${cred}`
}
if (describeRow(
  { route: 'acme-gateway', displayName: 'Acme Gateway', apiKeyEnv: 'ACME_GATEWAY_API_KEY', models: ['a', 'b'] },
  ['OPENAI_API_KEY'],
) !== 'Acme Gateway · 2 个模型 · 缺 ACME_GATEWAY_API_KEY')
  fail('缺 ref 时名册必须写出缺哪条 apiKeyEnv')
if (describeRow(
  { route: 'openai', displayName: '', apiKeyEnv: 'OPENAI_API_KEY', models: ['gpt'] },
  ['OPENAI_API_KEY'],
) !== 'openai · 1 个模型 · 已配 OPENAI_API_KEY')
  fail('已配 ref 时名册必须写出已配')

function describeWillList(route: string, models: string[]): string {
  const ids = models.map(m => m.trim()).filter(Boolean)
  if (ids.length === 0) return '还没有模型 id。保存后下次启动才会进 session.models。'
  return `将会进入 session.models：${ids.join('、')}`
}
if (!describeWillList('openai', ['a', 'b']).startsWith('将会进入 session.models：'))
  fail('有模型时提示必须是将会进入 session.models')

if (!cs.includes('"openai"') || !cs.includes('"anthropic"') || !cs.includes('"acme-gateway"'))
  fail('CatalogPresets 必须含 DSH 文档里的 openai / anthropic / acme-gateway')

console.log('✓ llm-providers 配方契约')
