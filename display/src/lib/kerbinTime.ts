const DAY = 21600;
const YEAR = 426 * DAY;
const pad = (value: number) => String(value).padStart(2, "0");

export function kerbinDate(ut: number): string {
  const year = Math.floor(ut / YEAR) + 1;
  const day = Math.floor((ut % YEAR) / DAY) + 1;
  const inDay = Math.floor(ut % DAY);
  return `Y${year} D${day} ${pad(Math.floor(inDay / 3600))}:${pad(Math.floor((inDay % 3600) / 60))}:${pad(inDay % 60)}`;
}

export function duration(seconds: number): string {
  const total = Math.round(seconds);
  const days = Math.floor(total / DAY);
  const hours = Math.floor((total % DAY) / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const rest = total % 60;
  if (days > 0) {
    return `${days}d ${hours}h ${minutes}m`;
  }
  return hours > 0 ? `${hours}h ${pad(minutes)}m ${pad(rest)}s` : `${minutes}m ${pad(rest)}s`;
}
