import { describe, expect, it } from 'vitest'
describe('UI safety', () => { it('encodes run ids before requesting detail endpoints', () => { expect(encodeURIComponent('a/b')).toBe('a%2Fb') }) })
