import { useState } from 'react';
import { GameCard } from './components/GameCard';
import { ScreenshotGrid } from './components/ScreenshotGrid';
import { FolderOpen, Settings, RefreshCw, Grid, Bell, GripVertical } from 'lucide-react';
import { PanelGroup, Panel, PanelResizeHandle } from 'react-resizable-panels';

interface Game {
  id: string;
  title: string;
  cover: string;
  publisher: string;
  screenshots: Array<{
    id: string;
    url: string;
    date: string;
  }>;
}

// Mock data for games
const games: Game[] = [
  {
    id: '1',
    title: 'Horizon Zero Dawn™ Remastered',
    cover: 'https://images.unsplash.com/photo-1761168942090-9d3d8f89cfb7?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxob3Jpem9uJTIwemVybyUyMGRhd24lMjBnYW1lfGVufDF8fHx8MTc3NDYxNTY4Nnww&ixlib=rb-4.1.0&q=80&w=1080',
    publisher: 'Sony Interactive',
    screenshots: [
      {
        id: 's1',
        url: 'https://images.unsplash.com/photo-1751703857389-b7d0cd1d5b54?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 28, 2026'
      },
      {
        id: 's2',
        url: 'https://images.unsplash.com/photo-1634763608027-f3dac114f24f?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 28, 2026'
      },
      {
        id: 's3',
        url: 'https://images.unsplash.com/photo-1773583202997-ef2946e087eb?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 28, 2026'
      },
      {
        id: 's4',
        url: 'https://images.unsplash.com/photo-1731865283223-04f577b3e9b2?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 28, 2026'
      },
      {
        id: 's5',
        url: 'https://images.unsplash.com/photo-1713710932124-bd6221114758?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 28, 2026'
      },
      {
        id: 's6',
        url: 'https://images.unsplash.com/photo-1564690778000-2893f1a60755?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 28, 2026'
      }
    ]
  },
  {
    id: '2',
    title: 'Diablo IV - Rise of Darkness',
    cover: 'https://images.unsplash.com/photo-1762217235246-4235328d882b?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
    publisher: 'Blizzard',
    screenshots: [
      {
        id: 's1',
        url: 'https://images.unsplash.com/photo-1564690778000-2893f1a60755?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 25, 2026'
      }
    ]
  },
  {
    id: '3',
    title: 'Death Stranding Directors Cut',
    cover: 'https://images.unsplash.com/photo-1452022449339-59005948ec5b?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
    publisher: 'Kojima Productions',
    screenshots: [
      {
        id: 's1',
        url: 'https://images.unsplash.com/photo-1773583202997-ef2946e087eb?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 20, 2026'
      }
    ]
  },
  {
    id: '4',
    title: 'Cyberpunk 2077',
    cover: 'https://images.unsplash.com/photo-1664092815283-19c6196f5319?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
    publisher: 'CD Projekt Red',
    screenshots: [
      {
        id: 's1',
        url: 'https://images.unsplash.com/photo-1713710932124-bd6221114758?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 15, 2026'
      }
    ]
  },
  {
    id: '5',
    title: 'Elden Ring',
    cover: 'https://images.unsplash.com/photo-1759688168277-185a0c623968?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
    publisher: 'FromSoftware',
    screenshots: [
      {
        id: 's1',
        url: 'https://images.unsplash.com/photo-1634763608027-f3dac114f24f?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 10, 2026'
      }
    ]
  },
  {
    id: '6',
    title: 'Starfield',
    cover: 'https://images.unsplash.com/photo-1531812494838-636e337af5a6?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
    publisher: 'Bethesda',
    screenshots: [
      {
        id: 's1',
        url: 'https://images.unsplash.com/photo-1751703857389-b7d0cd1d5b54?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 5, 2026'
      }
    ]
  },
  {
    id: '7',
    title: 'Forza Horizon 5',
    cover: 'https://images.unsplash.com/photo-1760553121003-93afc4d88ae0?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
    publisher: 'Playground Games',
    screenshots: [
      {
        id: 's1',
        url: 'https://images.unsplash.com/photo-1731865283223-04f577b3e9b2?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'March 1, 2026'
      }
    ]
  },
  {
    id: '8',
    title: 'Street Fighter 6',
    cover: 'https://images.unsplash.com/photo-1719169418120-277d5313c0d3?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
    publisher: 'Capcom',
    screenshots: [
      {
        id: 's1',
        url: 'https://images.unsplash.com/photo-1564690778000-2893f1a60755?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&q=80&w=1080',
        date: 'February 25, 2026'
      }
    ]
  }
];

export default function App() {
  const [selectedGame, setSelectedGame] = useState<Game>(games[0]);

  return (
    <div className="size-full flex flex-col bg-gray-900">
      {/* Top Navigation Bar */}
      <div className="h-16 bg-gray-800 border-b border-gray-700 flex items-center gap-2 px-4">
        <button className="px-4 py-2 bg-teal-700 hover:bg-teal-600 text-white rounded transition-colors">
          Import
        </button>
        <button className="px-4 py-2 bg-blue-700 hover:bg-blue-600 text-white rounded transition-colors">
          Import and Edit
        </button>
        <button className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors">
          Manual Import
        </button>
        <button className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors flex items-center gap-2">
          <Settings size={16} />
          Settings
        </button>
        <button className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors flex items-center gap-2">
          <RefreshCw size={16} />
          Refresh
        </button>
        <button className="ml-auto px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors relative">
          <Bell size={18} />
          <span className="absolute top-1 right-1 w-2 h-2 bg-red-500 rounded-full"></span>
        </button>
      </div>

      {/* Main Content */}
      <div className="flex-1 flex overflow-hidden">
        <PanelGroup direction="horizontal">
          {/* Left Sidebar - Game Library */}
          <Panel defaultSize={35} minSize={20} maxSize={60}>
            <div className="h-full border-r border-gray-700 bg-gray-850 flex flex-col">
              <div className="p-4 border-b border-gray-700">
                <input
                  type="text"
                  placeholder="Search"
                  className="w-full px-4 py-2 bg-gray-800 text-white rounded border border-gray-700 focus:outline-none focus:border-gray-500"
                />
              </div>
              <div className="flex gap-2 px-4 py-2 border-b border-gray-700">
                <button className="px-3 py-1 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded transition-colors">
                  Sort and Filter
                </button>
                <button className="px-3 py-1 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded transition-colors">
                  Recently Added
                </button>
                <button className="px-3 py-1 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded transition-colors">
                  Recently Played
                </button>
                <button className="ml-auto px-2 py-1 bg-gray-700 hover:bg-gray-600 text-white text-xs rounded transition-colors">
                  Rebuild
                </button>
                <button className="px-2 py-1 bg-gray-700 hover:bg-gray-600 text-white text-xs rounded transition-colors">
                  Fetch Covers
                </button>
              </div>
              <div className="flex-1 overflow-y-auto p-4">
                <div className="grid grid-cols-3 gap-4">
                  {games.map((game) => (
                    <GameCard
                      key={game.id}
                      id={game.id}
                      title={game.title}
                      cover={game.cover}
                      publisher={game.publisher}
                      isSelected={selectedGame.id === game.id}
                      onClick={() => setSelectedGame(game)}
                    />
                  ))}
                </div>
              </div>
            </div>
          </Panel>

          <PanelResizeHandle className="w-1 bg-gray-700 hover:bg-gray-500 transition-colors relative group">
            <div className="absolute inset-y-0 left-1/2 -translate-x-1/2 w-1 group-hover:w-1">
              <GripVertical className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 text-gray-500 group-hover:text-gray-300 opacity-0 group-hover:opacity-100 transition-opacity" size={20} />
            </div>
          </PanelResizeHandle>

          {/* Right Panel - Game Details & Screenshots */}
          <Panel defaultSize={65} minSize={40}>
            <div className="h-full overflow-y-auto bg-gray-900">
              <div className="p-6">
                {/* Game Header */}
                <div className="mb-6">
                  <div className="flex gap-6 mb-6">
                    <img
                      src={selectedGame.cover}
                      alt={selectedGame.title}
                      className="w-48 h-64 object-cover rounded-lg shadow-lg"
                    />
                    <div className="flex-1">
                      <h1 className="text-3xl text-white mb-2">{selectedGame.title}</h1>
                      <p className="text-gray-400 mb-4">{selectedGame.publisher}</p>
                      <div className="flex gap-3">
                        <button className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors flex items-center gap-2">
                          <FolderOpen size={16} />
                          Clean Folder
                        </button>
                        <button className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors flex items-center gap-2">
                          <Grid size={16} />
                          Scan Folder
                        </button>
                        <button className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors">
                          Edit Metadata
                        </button>
                      </div>
                    </div>
                  </div>
                </div>

                {/* Screenshots Section */}
                <div>
                  <div className="flex items-center justify-between mb-4">
                    <h2 className="text-xl text-white">All captures</h2>
                    <div className="flex items-center gap-4">
                      <span className="text-sm text-gray-400">Lightness</span>
                      <input
                        type="range"
                        min="0"
                        max="100"
                        defaultValue="50"
                        className="w-32"
                      />
                      <button className="px-3 py-1 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded transition-colors">
                        Grid
                      </button>
                    </div>
                  </div>
                  <p className="text-sm text-gray-400 mb-4">
                    {selectedGame.screenshots[0]?.date || 'No date'}
                  </p>
                  <ScreenshotGrid screenshots={selectedGame.screenshots} />
                </div>
              </div>
            </div>
          </Panel>
        </PanelGroup>
      </div>
    </div>
  );
}