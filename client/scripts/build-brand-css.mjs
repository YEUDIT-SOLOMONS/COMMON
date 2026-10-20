// מקמפל את src/brand.src.css ל-src/brand.css: היחידה "u" (= נקודת עיצוב אחת של קנבס 1366x768)
// הופכת ל-calc(N * var(--p)). ר' ההערה בראש brand.src.css.
import { readFileSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const root = join(dirname(fileURLToPath(import.meta.url)), '..', 'src')
const src = readFileSync(join(root, 'brand.src.css'), 'utf8')
const out = src.replace(/(?<![\w.#-])(-?\d*\.?\d+)u(?![\w-])/g, (_, n) => `calc(${n} * var(--p))`)
writeFileSync(join(root, 'brand.css'), `/* נוצר אוטומטית מ-brand.src.css - אין לערוך! (npm run build:brand) */\n${out}`)
console.log('brand.css written')
