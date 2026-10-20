// סימן המותג של common - שלושה עיגולים חופפים (ירוק מאחור, כחול, ורוד מלפנים) בדיוק לפי
// קובץ העיצוב של המעצבת (המיקום והגדלים נמדדו מהוקטורים שלו, אבל הצורות עיגוליות לגמרי (ב-PDF הן נמתחו)). הסימן סטטי - בלי תנועה.
//  - outline: גרסת קווי-מתאר בלבד (מילוי בצבע הרקע) כמו האייקון הקטן שליד "קהילת הצלמות של COMMON"
//  - children: אייקון שיושב במרכז העיגול הוורוד (נמצאת בדיוק במרכז התיבה של הסימן)
// הגודל נקבע ב-CSS (width) והיחס נשמר ע"י ה-viewBox.
export function BrandMark({ className = '', outline = false, children = null }) {
  return (
    <span className={`brand-mark ${outline ? 'brand-mark-outline' : ''} ${className}`.trim()} aria-hidden="true">
      <svg viewBox="0 0 229.2 211.2" focusable="false">
        <circle className="bm-green" cx="95.1" cy="95.1" r="95.1" />
        <circle className="bm-blue" cx="134.1" cy="116.1" r="95.1" />
        <circle className="bm-pink" cx="114.6" cy="105.65" r="87.65" />
      </svg>
      {children && <span className="brand-mark-glyph">{children}</span>}
    </span>
  )
}
