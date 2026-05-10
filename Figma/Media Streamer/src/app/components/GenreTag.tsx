interface GenreTagProps {
  label: string;
  onClick?: () => void;
}

export function GenreTag({ label, onClick }: GenreTagProps) {
  return (
    <span
      onClick={onClick}
      className={`inline-block px-3 py-1 bg-white/10 backdrop-blur-sm rounded-full text-sm text-gray-200 border border-white/20 ${
        onClick ? 'cursor-pointer hover:bg-white/20 transition-colors duration-200' : ''
      }`}
    >
      {label}
    </span>
  );
}
