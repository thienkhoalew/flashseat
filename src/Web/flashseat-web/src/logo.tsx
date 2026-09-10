import type { SVGProps } from 'react';

export function FlashSeatLogo({ size = 36, className, ...props }: SVGProps<SVGSVGElement> & { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 64 64"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      role="img"
      aria-label="FlashSeat logo"
      {...props}
    >
      <rect x="4" y="4" width="56" height="56" rx="17" fill="#15191F" />
      <rect x="4.75" y="4.75" width="54.5" height="54.5" rx="16.25" stroke="#2C3542" strokeWidth="1.5" />
      <path d="M20 16h26v8H29v7h14v8H29v15h-9V16Z" fill="#FF7052" />
      <path d="M43 15 27 39h9l-5 15 16-25h-9l5-14Z" fill="#FFF7EC" />
    </svg>
  );
}
