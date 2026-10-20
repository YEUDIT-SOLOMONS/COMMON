// שיעור המע"מ (תואם לשרת - Vat.Rate)
export const VAT_RATE = 0.18
export const VAT_PERCENT = Math.round(VAT_RATE * 100)

const round2 = (n) => Math.round(n * 100) / 100

export const withVat = (price) => round2(Number(price) * (1 + VAT_RATE))
export const beforeVat = (price) => round2(Number(price) / (1 + VAT_RATE))
