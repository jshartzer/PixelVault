import { Maximize2 } from 'lucide-react';

interface ScreenshotGridProps {
  screenshots: Array<{
    id: string;
    url: string;
    date: string;
  }>;
}

export function ScreenshotGrid({ screenshots }: ScreenshotGridProps) {
  return (
    <div className="grid grid-cols-2 gap-3">
      {screenshots.map((screenshot) => (
        <div
          key={screenshot.id}
          className="relative group cursor-pointer rounded-lg overflow-hidden bg-gray-800 aspect-video"
        >
          <img
            src={screenshot.url}
            alt={`Screenshot ${screenshot.id}`}
            className="w-full h-full object-cover transition-transform group-hover:scale-105"
          />
          <div className="absolute inset-0 bg-black/50 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center">
            <Maximize2 size={32} className="text-white" />
          </div>
        </div>
      ))}
    </div>
  );
}
