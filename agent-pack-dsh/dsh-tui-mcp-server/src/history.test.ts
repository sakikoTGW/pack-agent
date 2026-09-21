#!/usr/bin/env bun
import { compactHistory } from './history.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const huge = 'skill-name-'.repeat(4000)
const page = {
  hasMore: false,
  events: [
    {
      event: {
        type: 'user/message',
        seq: 1,
        time: 1,
        data: { content: [{ type: 'text', text: 'hello, are you awake' }] },
      },
    },
    {
      event: {
        type: 'turn/available_skills',
        seq: 2,
        time: 2,
        data: { catalog: huge },
      },
    },
    {
      event: {
        type: 'assistant/message',
        seq: 3,
        time: 3,
        data: { content: [{ type: 'text', text: 'awake' }] },
      },
    },
    {
      event: {
        type: 'llm/error',
        seq: 4,
        time: 4,
        data: { message: 'no API key for provider route "deepseek-official"' },
      },
    },
  ],
}

const compact = compactHistory(page)
if (compact.droppedTypes.includes('turn/available_skills') !== true) {
  fail(`droppedTypes ${JSON.stringify(compact.droppedTypes)}`)
}
const texts = compact.events.map((e) => e.text).filter(Boolean)
if (!texts.includes('hello, are you awake')) fail('user text')
if (!texts.includes('awake')) fail('assistant text')
if (!compact.events.some((e) => e.type === 'llm/error' && String(e.text).includes('no API key'))) {
  fail('llm error must survive')
}
const dumped = JSON.stringify(compact)
if (dumped.includes(huge.slice(0, 80))) fail('skill catalog leaked into compact dump')
if (dumped.length > 20_000) fail(`compact still huge: ${dumped.length}`)

const reminder = compactHistory({
  events: [{
    event: {
      type: 'user/message',
      seq: 9,
      data: {
        content: [{
          type: 'text',
          text: `hi\n<available_skills>\n- audit: ${'x'.repeat(8000)}\n</available_skills>\nend`,
        }],
      },
    },
  }],
})
const reminderText = reminder.events[0]?.text ?? ''
if (reminderText.includes('x'.repeat(40))) fail('skills body leaked')
if (!reminderText.includes('[available_skills omitted]')) fail(`skills not stripped: ${reminderText.slice(0, 80)}`)

console.log('✓ history compact')
