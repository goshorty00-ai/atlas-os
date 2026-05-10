import { ReactNode } from 'react';

interface ContentRowProps {
  title: string;
  children: ReactNode;
  onSeeAll?: () => void;
}

export function ContentRow({ title, children, onSeeAll }: ContentRowProps) {
  return (
    <div className="mb-8">
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-white text-2xl font-semibold">{title}</h2>
        {onSeeAll && (
          <button
            onClick={onSeeAll}
            className="text-purple-400 hover:text-purple-300 text-sm font-medium transition-colors duration-200 flex items-center gap-1"
          >
            See All
            <svg
              width="16"
              height="16"
              viewBox="0 0 16 16"
              fill="none"
              xmlns="http://www.w3.org/2000/svg"
            >
              <path
                d="M6 12L10 8L6 4"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
          </button>
        )}
      </div>
      <div className="flex gap-4 overflow-x-auto pb-4 scrollbar-hide">
        {children}
      </div>
    </div>
  );
}
