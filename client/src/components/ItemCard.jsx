import { CategoryIcon } from './CategoryIcon.jsx'
import { MapPinIcon } from './icons.jsx'
import { iconTextsForItem } from '../utils/categoryIcon.js'
import { CardArrowGlyph } from './glyphs.jsx'
import { ItemStatusBadge } from './StatusBadge.jsx'
import { FavoriteHeart } from './FavoriteHeart.jsx'
import { Stars } from './Stars.jsx'
import { Price } from './Price.jsx'

// הלב יושב מחוץ לכפתור הכרטיס (כפתור בתוך כפתור אינו תקין), ממוקם מעל פינת התמונה
export function ItemCard({ item, onClick, style, categoryChains, rating }) {
  return (
    <div className="item-card-shell" style={style}>
      <button type="button" className="item-card" onClick={onClick}>
        <div className="item-card-media">
          {item.imageDataUrl ? (
            <img src={item.imageDataUrl} alt={item.name} className="item-card-image" loading="lazy" decoding="async" />
          ) : (
            <CategoryIcon className="item-card-icon" texts={iconTextsForItem(item, categoryChains)} />
          )}
          {item.forSale ? (
            <span className="badge badge-sale item-card-status">למכירה</span>
          ) : (
            <ItemStatusBadge status={item.status} className="item-card-status" />
          )}
          {item.city && (
            <span className="badge item-card-city-badge" title={item.city}>
              <MapPinIcon className="item-card-city-badge-icon" />
              <span className="item-card-city-badge-text">{item.city}</span>
            </span>
          )}
          {(item.imageCount ?? item.imageDataUrls?.length ?? 0) > 1 && (
            <span className="item-card-photos">{item.imageCount ?? item.imageDataUrls.length} תמונות</span>
          )}
        </div>
        <h3 className="item-card-title">{item.name}</h3>
        {rating?.count > 0 && (
          <span className="item-card-rating">
            <Stars value={rating.average} /> <span className="item-card-rating-count">({rating.count})</span>
          </span>
        )}
        <p className="item-card-desc">{item.description}</p>
        <div className="item-card-footer">
          <span className="item-price">
            {item.forSale ? (
              <Price amount={item.salePrice} unit=" · למכירה" />
            ) : item.isLocation ? (
              <Price amount={item.hourlyPrice} unit=" / שעה" />
            ) : (
              <Price amount={item.dailyPrice} />
            )}
          </span>
          <span className="item-card-cta">
            לפרטים
            <CardArrowGlyph className="item-card-arrow" />
          </span>
        </div>
      </button>
      <div className="item-card-heart-anchor">
        <FavoriteHeart item={item} />
      </div>
    </div>
  )
}
