// ממיר קובץ תמונה שנבחר ע"י המשתמש ל-Data URL (Base64) לאחר שינוי גודל וכיווץ בצד הלקוח
// (בעזרת canvas), כדי לשמור על גודל סביר לפני שליחה לשרת - Firestore מגביל מסמך בודד ל-1MB,
// ואנחנו שומרים את התמונה ישירות כשדה מחרוזת במסמך הפריט (ראו Item.ImageDataUrl בשרת).
export function resizeImageToDataUrl(file, { maxDimension = 800, quality = 0.72 } = {}) {
  return new Promise((resolve, reject) => {
    if (!file.type.startsWith('image/')) {
      reject(new Error('יש לבחור קובץ תמונה'))
      return
    }

    const reader = new FileReader()
    reader.onerror = () => reject(new Error('קריאת קובץ התמונה נכשלה'))
    reader.onload = () => {
      const img = new Image()
      img.onerror = () => reject(new Error('טעינת התמונה נכשלה'))
      img.onload = () => {
        let { width, height } = img
        if (width > maxDimension || height > maxDimension) {
          const scale = maxDimension / Math.max(width, height)
          width = Math.round(width * scale)
          height = Math.round(height * scale)
        }

        const canvas = document.createElement('canvas')
        canvas.width = width
        canvas.height = height
        const ctx = canvas.getContext('2d')
        ctx.drawImage(img, 0, 0, width, height)

        resolve(canvas.toDataURL('image/jpeg', quality))
      }
      img.src = reader.result
    }
    reader.readAsDataURL(file)
  })
}
