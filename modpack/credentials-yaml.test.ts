#!/usr/bin/env bun
/**
 * DSH credentials.yaml：DEEPSEEK_API_KEY 的 REF 行。PAD 凭据框必须按这个读写。
 */
import { hasDeepseekKey, upsertDeepseekKey, envHasDeepseekKey, upsertRef, removeRef, listRefs } from './credentials-yaml.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

if (hasDeepseekKey('')) fail('empty yaml is not configured')
if (hasDeepseekKey('openai:\n  apiKey: sk\n')) fail('other keys are not DEEPSEEK_API_KEY')
if (hasDeepseekKey('DEEPSEEK_API_KEY: "REF: "\n')) fail('empty REF is not configured')
if (!hasDeepseekKey('DEEPSEEK_API_KEY: "REF: sk-test"\n')) fail('quoted REF')
if (!hasDeepseekKey('DEEPSEEK_API_KEY: REF: sk-test\n')) fail('bare REF')
if (!hasDeepseekKey('DEEPSEEK_API_KEY: sk-raw\n')) fail('raw value')

const once = upsertDeepseekKey('', 'sk-one')
if (!once.includes('DEEPSEEK_API_KEY: "REF: sk-one"')) fail(`upsert empty: ${once}`)
if (!hasDeepseekKey(once)) fail('upsert result must parse')

const twice = upsertDeepseekKey(once, 'sk-two')
if (twice.includes('sk-one')) fail('old value must be replaced')
if (!hasDeepseekKey(twice)) fail('second upsert')
if ((twice.match(/DEEPSEEK_API_KEY/g) ?? []).length !== 1) fail('must keep a single key line')

if (envHasDeepseekKey('FOO=1\n')) fail('other env')
if (!envHasDeepseekKey('DEEPSEEK_API_KEY=sk-env\n')) fail('env line')
if (envHasDeepseekKey('# DEEPSEEK_API_KEY=sk\n')) fail('commented env')

const openai = upsertRef('', 'OPENAI_API_KEY', 'sk-oai')
if (!openai.includes('OPENAI_API_KEY: "REF: sk-oai"')) fail('upsertRef openai')
const both = upsertRef(openai, 'DEEPSEEK_API_KEY', 'sk-ds')
if (!listRefs(both).includes('OPENAI_API_KEY') || !listRefs(both).includes('DEEPSEEK_API_KEY'))
  fail('listRefs two keys')
const dropped = removeRef(both, 'OPENAI_API_KEY')
if (dropped.includes('OPENAI_API_KEY')) fail('removeRef left openai')
if (!hasDeepseekKey(dropped)) fail('removeRef dropped the wrong line')
try {
  upsertRef('', 'not-an-env', 'x')
  fail('bad ref name must throw')
} catch (e) {
  if (!(e instanceof Error) || !e.message.includes('ENV-style')) fail(`bad name: ${e}`)
}

console.log('✓ credentials-yaml DEEPSEEK_API_KEY')
