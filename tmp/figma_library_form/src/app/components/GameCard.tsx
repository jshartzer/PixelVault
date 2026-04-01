import { Play } from 'lucide-react';

interface GameCardProps {
  id: string;
  title: string;
  cover: string;
  publisher?: string;
  isSelected: boolean;
  onClick: () => void;
}

export function GameCard({ title, cover, publisher, isSelected, onClick }: GameCardProps) {
  return (
    <div
      onClick={onClick}
      className={`relative cursor-pointer rounded-lg overflow-hidden transition-all ${
        isSelected ? 'ring-2 ring-gray-400' : 'hover:ring-2 hover:ring-gray-600'
      }`}
    >
      <div className="aspect-[3/4] relative group">
        <img
          src={cover}
          alt={title}
          className="w-full h-full object-cover"
        />
        <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-transparent to-transparent opacity-0 group-hover:opacity-100 transition-opacity">
          <div className="absolute bottom-0 left-0 right-0 p-3">
            <div className="flex items-center justify-center gap-2 bg-gray-600 hover:bg-gray-500 text-white py-2 px-4 rounded-md transition-colors">
              <Play size={16} />
              <span className="text-sm">Play</span>
            </div>
          </div>
        </div>
      </div>
      <div className="p-2 bg-gray-800">
        <h3 className="text-sm text-white truncate">{title}</h3>
        {publisher && <p className="text-xs text-gray-400 truncate">{publisher}</p>}
      </div>
    </div>
  );
}