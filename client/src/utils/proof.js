import { resizeImageToDataUrl } from './image.js'

const MAX_PDF_BYTES = 600 * 1024

// קבצי אקסל כאסמכתא (דוח העברה מהבנק לרוב מגיע כ-xlsx/csv): סוג הקובץ נקבע לפי הסיומת, כי הדפדפן לא תמיד מזהה אותו
const SPREADSHEET_TYPES = {
  xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  xls: 'application/vnd.ms-excel',
  csv: 'text/csv',
}

function readAsDataUrl(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onerror = () => reject(new Error('קריאת הקובץ נכשלה'))
    reader.onload = () => resolve(reader.result)
    reader.readAsDataURL(file)
  })
}

// קורא קובץ אסמכתא של העברה בנקאית: תמונה (מכווצת בדפדפן לפני השליחה) או PDF עד 600KB.
// מחזיר { dataUrl, fileName, contentType } להעלאה לשרת.
export async function readProofFile(file) {
  const extension = file.name.split('.').pop()?.toLowerCase() ?? ''
  const spreadsheetType = SPREADSHEET_TYPES[extension]
  if (spreadsheetType) {
    if (file.size > MAX_PDF_BYTES) throw new Error('קובץ האקסל גדול מדי (עד 600KB)')
    const raw = await readAsDataUrl(file)
    return { dataUrl: `data:${spreadsheetType};base64,${raw.split(',')[1]}`, fileName: file.name, contentType: spreadsheetType }
  }

  if (file.type === 'application/pdf') {
    if (file.size > MAX_PDF_BYTES) throw new Error('קובץ ה-PDF גדול מדי (עד 600KB) - אפשר לצלם מסך של האסמכתא ולהעלות תמונה')
    return { dataUrl: await readAsDataUrl(file), fileName: file.name, contentType: file.type }
  }

  if (file.type.startsWith('image/')) {
    const dataUrl = await resizeImageToDataUrl(file, { maxDimension: 1400, quality: 0.72 })
    return { dataUrl, fileName: file.name.replace(/\.[^.]+$/, '') + '.jpg', contentType: 'image/jpeg' }
  }

  throw new Error('אפשר להעלות תמונה, PDF או קובץ אקסל בלבד')
}

// פותח אסמכתא (Data URL): תמונה ו-PDF נפתחים בלשונית חדשה, וקובץ אקסל יורד למחשב
export function openDataUrl(dataUrl, fileName = 'asmachta') {
  const [header, base64] = dataUrl.split(',')
  const contentType = /data:([^;]+)/.exec(header)?.[1] ?? 'application/octet-stream'
  const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0))
  const url = URL.createObjectURL(new Blob([bytes], { type: contentType }))

  if (contentType.startsWith('image/') || contentType === 'application/pdf') {
    window.open(url, '_blank', 'noopener')
    return
  }

  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 10000)
}
