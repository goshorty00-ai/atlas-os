export interface Track {
  id: string;
  number: number;
  title: string;
  duration: string;
  audioUrl?: string;
  filePath?: string;
  discNumber?: number;
  trackNumber?: number;
  artist?: string;
  year?: number;
  genres?: string[];
  mbRecordingId?: string;
  bpm?: number;
  key?: string;
  energy?: number;
}

export interface Album {
  id: string;
  title: string;
  artist: string;
  year: string;
  genre: string[];
  cover: string;
  tracks: Track[];
  duration: string;
  detailsStatusText?: string;
  metaProvider?: string;
  metaUrl?: string;
  mbRelease?: {
    id?: string;
    provider?: string;
    url?: string;
    date?: string;
    country?: string;
    label?: string;
    barcode?: string;
    status?: string;
    packaging?: string;
  };
  mood: {
    energetic: number;
    melancholic: number;
    uplifting: number;
    aggressive: number;
  };
  dominantColor: string;
}

export const albums: Album[] = [
  {
    id: '1',
    title: 'Neon Dreams',
    artist: 'Synthwave Collective',
    year: '2029',
    genre: ['Synthwave', 'Electronic'],
    cover: 'https://images.unsplash.com/photo-1637121822056-51811ef1d92a?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxzeW50aHdhdmUlMjBhbGJ1bSUyMGFydHxlbnwxfHx8fDE3NzI2Mzc5NTd8MA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '43:22',
    mood: {
      energetic: 78,
      melancholic: 45,
      uplifting: 82,
      aggressive: 22,
    },
    dominantColor: '#8B5CF6',
    tracks: [
      { id: 't1', number: 1, title: 'Midnight Drive', duration: '4:32', bpm: 124, key: 'F# Minor', energy: 82 },
      { id: 't2', number: 2, title: 'Neon Skyline', duration: '3:45', bpm: 128, key: 'D Major', energy: 76 },
      { id: 't3', number: 3, title: 'Digital Rain', duration: '5:12', bpm: 118, key: 'A Minor', energy: 68 },
      { id: 't4', number: 4, title: 'Chrome Hearts', duration: '4:18', bpm: 132, key: 'E Minor', energy: 85 },
      { id: 't5', number: 5, title: 'Virtual Sunset', duration: '6:02', bpm: 115, key: 'G Major', energy: 62 },
      { id: 't6', number: 6, title: 'Laser Dreams', duration: '3:58', bpm: 130, key: 'C# Minor', energy: 79 },
      { id: 't7', number: 7, title: 'Retro Future', duration: '4:44', bpm: 126, key: 'B Minor', energy: 74 },
      { id: 't8', number: 8, title: 'Electric Paradise', duration: '5:21', bpm: 122, key: 'F Major', energy: 88 },
      { id: 't9', number: 9, title: 'Cosmic Highway', duration: '5:30', bpm: 120, key: 'D# Minor', energy: 70 },
    ],
  },
  {
    id: '2',
    title: 'Urban Echoes',
    artist: 'The Cipher',
    year: '2028',
    genre: ['Hip Hop', 'Rap'],
    cover: 'https://images.unsplash.com/photo-1646900614911-378fd0c1d86d?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxoaXAlMjBob3AlMjBhbGJ1bSUyMGNvdmVyJTIwYXJ0fGVufDF8fHx8MTc3MjU5MDIxN3ww&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '38:45',
    mood: {
      energetic: 85,
      melancholic: 35,
      uplifting: 65,
      aggressive: 72,
    },
    dominantColor: '#3B82F6',
    tracks: [
      { id: 't10', number: 1, title: 'City Lights', duration: '3:22', bpm: 95, key: 'G Minor', energy: 78 },
      { id: 't11', number: 2, title: 'Street Philosophy', duration: '4:15', bpm: 88, key: 'C Minor', energy: 82 },
      { id: 't12', number: 3, title: 'Concrete Jungle', duration: '3:48', bpm: 92, key: 'D Minor', energy: 88 },
      { id: 't13', number: 4, title: 'Hustle & Flow', duration: '4:02', bpm: 98, key: 'A Minor', energy: 85 },
      { id: 't14', number: 5, title: 'Golden Era', duration: '3:35', bpm: 90, key: 'F Minor', energy: 75 },
      { id: 't15', number: 6, title: 'Dreams in Motion', duration: '5:12', bpm: 85, key: 'Eb Minor', energy: 68 },
      { id: 't16', number: 7, title: 'Rise Up', duration: '3:58', bpm: 96, key: 'Bb Minor', energy: 90 },
      { id: 't17', number: 8, title: 'Night Shift', duration: '4:28', bpm: 87, key: 'E Minor', energy: 72 },
      { id: 't18', number: 9, title: 'Legacy', duration: '6:05', bpm: 82, key: 'G# Minor', energy: 65 },
    ],
  },
  {
    id: '3',
    title: 'Echoes of Tomorrow',
    artist: 'Luna Grace',
    year: '2029',
    genre: ['Indie', 'Alternative'],
    cover: 'https://images.unsplash.com/photo-1677391939908-28b8ae9dc2cc?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxpbmRpZSUyMHJvY2slMjBhbGJ1bSUyMGFydHdvcmt8ZW58MXx8fHwxNzcyNjM3OTU4fDA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '41:18',
    mood: {
      energetic: 55,
      melancholic: 72,
      uplifting: 58,
      aggressive: 28,
    },
    dominantColor: '#EC4899',
    tracks: [
      { id: 't19', number: 1, title: 'Whispers in the Wind', duration: '4:12', bpm: 110, key: 'E Major', energy: 52 },
      { id: 't20', number: 2, title: 'Broken Clocks', duration: '3:45', bpm: 105, key: 'A Major', energy: 48 },
      { id: 't21', number: 3, title: 'Painted Skies', duration: '5:28', bpm: 98, key: 'D Major', energy: 62 },
      { id: 't22', number: 4, title: 'Silent Storms', duration: '4:35', bpm: 112, key: 'G Major', energy: 55 },
      { id: 't23', number: 5, title: 'Fading Memories', duration: '3:58', bpm: 100, key: 'C Major', energy: 45 },
      { id: 't24', number: 6, title: 'Chasing Shadows', duration: '4:48', bpm: 108, key: 'F Major', energy: 68 },
      { id: 't25', number: 7, title: 'Lost in Translation', duration: '5:15', bpm: 95, key: 'Bb Major', energy: 42 },
      { id: 't26', number: 8, title: 'New Horizons', duration: '4:22', bpm: 115, key: 'Ab Major', energy: 72 },
      { id: 't27', number: 9, title: 'Homeward', duration: '4:55', bpm: 102, key: 'Eb Major', energy: 58 },
    ],
  },
  {
    id: '4',
    title: 'Midnight Jazz Sessions',
    artist: 'The Modern Quartet',
    year: '2027',
    genre: ['Jazz', 'Contemporary'],
    cover: 'https://images.unsplash.com/photo-1771301455501-694654813e1a?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxqYXp6JTIwYWxidW0lMjBjb3ZlciUyMHZpbnRhZ2V8ZW58MXx8fHwxNzcyNjM3OTU4fDA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '52:34',
    mood: {
      energetic: 45,
      melancholic: 62,
      uplifting: 52,
      aggressive: 15,
    },
    dominantColor: '#F59E0B',
    tracks: [
      { id: 't28', number: 1, title: 'Blue Note Serenade', duration: '6:45', bpm: 72, key: 'Bb Major', energy: 42 },
      { id: 't29', number: 2, title: 'Velvet Lounge', duration: '5:32', bpm: 68, key: 'Eb Major', energy: 38 },
      { id: 't30', number: 3, title: 'Smoky After Hours', duration: '7:18', bpm: 65, key: 'F Major', energy: 35 },
      { id: 't31', number: 4, title: 'City Lights Swing', duration: '4:55', bpm: 125, key: 'G Major', energy: 65 },
      { id: 't32', number: 5, title: 'Bourbon Street Blues', duration: '6:28', bpm: 70, key: 'C Minor', energy: 48 },
      { id: 't33', number: 6, title: 'Moonlit Improvisation', duration: '8:12', bpm: 62, key: 'D Minor', energy: 32 },
      { id: 't34', number: 7, title: 'Sophisticated Lady', duration: '5:45', bpm: 75, key: 'Ab Major', energy: 52 },
      { id: 't35', number: 8, title: 'Take Five Plus One', duration: '7:39', bpm: 168, key: 'Eb Minor', energy: 58 },
    ],
  },
  {
    id: '5',
    title: 'Symphony No. 9',
    artist: 'Virtuoso Orchestra',
    year: '2026',
    genre: ['Classical', 'Orchestral'],
    cover: 'https://images.unsplash.com/photo-1465847899084-d164df4dedc6?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxjbGFzc2ljYWwlMjBtdXNpYyUyMGNvbmNlcnR8ZW58MXx8fHwxNzcyNjMxNzQ1fDA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '64:28',
    mood: {
      energetic: 65,
      melancholic: 55,
      uplifting: 78,
      aggressive: 45,
    },
    dominantColor: '#10B981',
    tracks: [
      { id: 't36', number: 1, title: 'Allegro Maestoso', duration: '18:45', bpm: 88, key: 'D Major', energy: 72 },
      { id: 't37', number: 2, title: 'Andante Cantabile', duration: '12:22', bpm: 60, key: 'G Major', energy: 38 },
      { id: 't38', number: 3, title: 'Scherzo: Vivace', duration: '8:35', bpm: 144, key: 'D Minor', energy: 85 },
      { id: 't39', number: 4, title: 'Finale: Presto', duration: '24:46', bpm: 96, key: 'D Major', energy: 92 },
    ],
  },
  {
    id: '6',
    title: 'Starlight',
    artist: 'Nova Dreams',
    year: '2029',
    genre: ['Pop', 'Electronic'],
    cover: 'https://images.unsplash.com/photo-1759455409415-9d91d286fe47?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxwb3AlMjBtdXNpYyUyMGFsYnVtJTIwYXJ0fGVufDF8fHx8MTc3MjYzNzk1OXww&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '35:42',
    mood: {
      energetic: 88,
      melancholic: 25,
      uplifting: 92,
      aggressive: 18,
    },
    dominantColor: '#EC4899',
    tracks: [
      { id: 't40', number: 1, title: 'Dancing on Clouds', duration: '3:28', bpm: 128, key: 'C Major', energy: 90 },
      { id: 't41', number: 2, title: 'Starlight', duration: '3:52', bpm: 124, key: 'G Major', energy: 88 },
      { id: 't42', number: 3, title: 'Forever Young', duration: '4:15', bpm: 120, key: 'D Major', energy: 85 },
      { id: 't43', number: 4, title: 'Electric Love', duration: '3:38', bpm: 132, key: 'A Major', energy: 92 },
      { id: 't44', number: 5, title: 'Crystal Dreams', duration: '4:02', bpm: 126, key: 'E Major', energy: 82 },
      { id: 't45', number: 6, title: 'Summer Nights', duration: '3:45', bpm: 118, key: 'F Major', energy: 78 },
      { id: 't46', number: 7, title: 'Heart Beats', duration: '3:22', bpm: 130, key: 'Bb Major', energy: 88 },
      { id: 't47', number: 8, title: 'Into the Light', duration: '4:28', bpm: 122, key: 'Ab Major', energy: 86 },
      { id: 't48', number: 9, title: 'Euphoria', duration: '4:52', bpm: 128, key: 'Eb Major', energy: 95 },
    ],
  },
  {
    id: '7',
    title: 'Techno Horizons',
    artist: 'Binary Code',
    year: '2029',
    genre: ['Techno', 'House'],
    cover: 'https://images.unsplash.com/photo-1623307019152-1ee797183f1d?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHx0ZWNobm8lMjBhbGJ1bSUyMGNvdmVyJTIwbmVvbnxlbnwxfHx8fDE3NzI2Mzc5NTl8MA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '48:15',
    mood: {
      energetic: 95,
      melancholic: 15,
      uplifting: 75,
      aggressive: 68,
    },
    dominantColor: '#06B6D4',
    tracks: [
      { id: 't49', number: 1, title: 'Machine Dreams', duration: '6:32', bpm: 138, key: 'A Minor', energy: 92 },
      { id: 't50', number: 2, title: 'Digital Underground', duration: '5:45', bpm: 142, key: 'E Minor', energy: 95 },
      { id: 't51', number: 3, title: 'Pulse', duration: '7:18', bpm: 135, key: 'D Minor', energy: 88 },
      { id: 't52', number: 4, title: 'Frequency Shift', duration: '6:05', bpm: 140, key: 'G Minor', energy: 90 },
      { id: 't53', number: 5, title: 'Acid Rain', duration: '5:28', bpm: 136, key: 'C Minor', energy: 94 },
      { id: 't54', number: 6, title: 'Bass Mechanics', duration: '6:52', bpm: 138, key: 'F Minor', energy: 96 },
      { id: 't55', number: 7, title: 'Synth Revolution', duration: '5:15', bpm: 144, key: 'Bb Minor', energy: 92 },
      { id: 't56', number: 8, title: 'Future Shock', duration: '5:00', bpm: 140, key: 'Eb Minor', energy: 98 },
    ],
  },
  {
    id: '8',
    title: 'Ambient Spaces',
    artist: 'Ethereal Sound',
    year: '2028',
    genre: ['Ambient', 'Electronic'],
    cover: 'https://images.unsplash.com/photo-1676483752612-69cd502fd103?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxhbWJpZW50JTIwbXVzaWMlMjBhcnR3b3JrfGVufDF8fHx8MTc3MjYzNzk2MHww&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '58:42',
    mood: {
      energetic: 22,
      melancholic: 48,
      uplifting: 65,
      aggressive: 8,
    },
    dominantColor: '#8B5CF6',
    tracks: [
      { id: 't57', number: 1, title: 'Floating', duration: '8:45', bpm: 65, key: 'C Major', energy: 18 },
      { id: 't58', number: 2, title: 'Deep Space', duration: '9:22', bpm: 58, key: 'F Major', energy: 15 },
      { id: 't59', number: 3, title: 'Meditation', duration: '7:35', bpm: 62, key: 'G Major', energy: 20 },
      { id: 't60', number: 4, title: 'Ocean Waves', duration: '6:28', bpm: 60, key: 'D Major', energy: 22 },
      { id: 't61', number: 5, title: 'Celestial', duration: '8:12', bpm: 55, key: 'A Major', energy: 25 },
      { id: 't62', number: 6, title: 'Tranquility', duration: '7:48', bpm: 58, key: 'E Major', energy: 18 },
      { id: 't63', number: 7, title: 'Dreamscape', duration: '10:32', bpm: 52, key: 'Bb Major', energy: 16 },
    ],
  },
  {
    id: '9',
    title: 'Electric Storm',
    artist: 'Voltage',
    year: '2029',
    genre: ['Rock', 'Alternative'],
    cover: 'https://images.unsplash.com/photo-1590310051055-1079d8f48c89?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxyb2NrJTIwYmFuZCUyMGFsYnVtJTIwY292ZXJ8ZW58MXx8fHwxNzcyNjIzMTc4fDA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '42:35',
    mood: {
      energetic: 92,
      melancholic: 38,
      uplifting: 68,
      aggressive: 85,
    },
    dominantColor: '#EF4444',
    tracks: [
      { id: 't64', number: 1, title: 'Thunderstruck', duration: '4:12', bpm: 145, key: 'E Minor', energy: 95 },
      { id: 't65', number: 2, title: 'Lightning Strikes', duration: '3:45', bpm: 152, key: 'A Minor', energy: 92 },
      { id: 't66', number: 3, title: 'Eye of the Storm', duration: '5:28', bpm: 138, key: 'D Minor', energy: 88 },
      { id: 't67', number: 4, title: 'Electric Fever', duration: '3:58', bpm: 148, key: 'G Minor', energy: 90 },
      { id: 't68', number: 5, title: 'Wild Hearts', duration: '4:35', bpm: 142, key: 'C Minor', energy: 86 },
      { id: 't69', number: 6, title: 'Rage On', duration: '4:48', bpm: 155, key: 'F Minor', energy: 98 },
      { id: 't70', number: 7, title: 'Burning Skies', duration: '5:15', bpm: 140, key: 'Bb Minor', energy: 92 },
      { id: 't71', number: 8, title: 'Revolution', duration: '4:22', bpm: 150, key: 'Eb Minor', energy: 94 },
      { id: 't72', number: 9, title: 'The Final Strike', duration: '6:12', bpm: 135, key: 'B Minor', energy: 96 },
    ],
  },
  {
    id: '10',
    title: 'Island Vibes',
    artist: 'Tropical Sounds',
    year: '2028',
    genre: ['Reggae', 'World'],
    cover: 'https://images.unsplash.com/photo-1759340875613-070b317265f8?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxyZWdnYWUlMjBhbGJ1bSUyMGFydCUyMHRyb3BpY2FsfGVufDF8fHx8MTc3MjYzNzk2MHww&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '40:18',
    mood: {
      energetic: 58,
      melancholic: 22,
      uplifting: 88,
      aggressive: 12,
    },
    dominantColor: '#10B981',
    tracks: [
      { id: 't73', number: 1, title: 'Sunshine Reggae', duration: '4:05', bpm: 85, key: 'G Major', energy: 62 },
      { id: 't74', number: 2, title: 'One Love', duration: '3:48', bpm: 78, key: 'C Major', energy: 55 },
      { id: 't75', number: 3, title: 'Coconut Dreams', duration: '4:32', bpm: 82, key: 'D Major', energy: 58 },
      { id: 't76', number: 4, title: 'Beach Party', duration: '3:55', bpm: 88, key: 'A Major', energy: 68 },
      { id: 't77', number: 5, title: 'Tropical Paradise', duration: '5:12', bpm: 80, key: 'E Major', energy: 52 },
      { id: 't78', number: 6, title: 'Island Rhythm', duration: '4:28', bpm: 84, key: 'F Major', energy: 60 },
      { id: 't79', number: 7, title: 'Caribbean Nights', duration: '4:45', bpm: 76, key: 'Bb Major', energy: 48 },
      { id: 't80', number: 8, title: 'Summer Breeze', duration: '4:18', bpm: 82, key: 'Ab Major', energy: 55 },
      { id: 't81', number: 9, title: 'Good Vibes Only', duration: '5:15', bpm: 86, key: 'Eb Major', energy: 65 },
    ],
  },
  {
    id: '11',
    title: 'Dark Matter',
    artist: 'Obsidian',
    year: '2029',
    genre: ['Metal', 'Progressive'],
    cover: 'https://images.unsplash.com/photo-1673041535294-0cbbc376e379?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxtZXRhbCUyMGFsYnVtJTIwY292ZXIlMjBkYXJrfGVufDF8fHx8MTc3MjYzNzk2MXww&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '51:28',
    mood: {
      energetic: 88,
      melancholic: 65,
      uplifting: 32,
      aggressive: 95,
    },
    dominantColor: '#DC2626',
    tracks: [
      { id: 't82', number: 1, title: 'Into the Void', duration: '6:45', bpm: 185, key: 'E Minor', energy: 92 },
      { id: 't83', number: 2, title: 'Blackened Sun', duration: '5:32', bpm: 178, key: 'D Minor', energy: 95 },
      { id: 't84', number: 3, title: 'Eternal Darkness', duration: '7:18', bpm: 165, key: 'C# Minor', energy: 88 },
      { id: 't85', number: 4, title: 'Shattered Reality', duration: '6:05', bpm: 192, key: 'F# Minor', energy: 98 },
      { id: 't86', number: 5, title: 'Iron Will', duration: '5:28', bpm: 172, key: 'B Minor', energy: 90 },
      { id: 't87', number: 6, title: 'Storm of Chaos', duration: '6:52', bpm: 188, key: 'A Minor', energy: 96 },
      { id: 't88', number: 7, title: 'Descent', duration: '8:15', bpm: 160, key: 'G Minor', energy: 85 },
      { id: 't89', number: 8, title: 'Final Hour', duration: '5:13', bpm: 180, key: 'Eb Minor', energy: 94 },
    ],
  },
  {
    id: '12',
    title: 'Soul Revival',
    artist: 'The Groove Masters',
    year: '2027',
    genre: ['Soul', 'R&B'],
    cover: 'https://images.unsplash.com/photo-1624913763354-1a6feee6858d?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxzb3VsJTIwbXVzaWMlMjB2aW55bCUyMHJlY29yZHxlbnwxfHx8fDE3NzI2Mzc5NjF8MA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '44:52',
    mood: {
      energetic: 68,
      melancholic: 45,
      uplifting: 82,
      aggressive: 25,
    },
    dominantColor: '#F59E0B',
    tracks: [
      { id: 't90', number: 1, title: 'Smooth Operator', duration: '4:32', bpm: 92, key: 'Eb Major', energy: 65 },
      { id: 't91', number: 2, title: 'Midnight Groove', duration: '5:15', bpm: 88, key: 'Ab Major', energy: 62 },
      { id: 't92', number: 3, title: 'Love Jones', duration: '4:48', bpm: 85, key: 'Bb Major', energy: 58 },
      { id: 't93', number: 4, title: 'Funky Feelings', duration: '3:58', bpm: 98, key: 'F Major', energy: 78 },
      { id: 't94', number: 5, title: 'Sweet Sensation', duration: '4:25', bpm: 90, key: 'C Major', energy: 68 },
      { id: 't95', number: 6, title: 'Heart & Soul', duration: '5:38', bpm: 82, key: 'G Major', energy: 55 },
      { id: 't96', number: 7, title: 'Velvet Voice', duration: '4:12', bpm: 86, key: 'D Major', energy: 60 },
      { id: 't97', number: 8, title: 'Rhythm Divine', duration: '5:28', bpm: 95, key: 'A Major', energy: 75 },
      { id: 't98', number: 9, title: 'Soul Shine', duration: '6:36', bpm: 80, key: 'E Major', energy: 72 },
    ],
  },
  {
    id: '13',
    title: 'Country Roads',
    artist: 'Nashville Stars',
    year: '2028',
    genre: ['Country', 'Folk'],
    cover: 'https://images.unsplash.com/photo-1598038882537-41e842034f17?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxjb3VudHJ5JTIwbXVzaWMlMjBhbGJ1bXxlbnwxfHx8fDE3NzI2Mzc5NjF8MA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '39:24',
    mood: {
      energetic: 55,
      melancholic: 58,
      uplifting: 72,
      aggressive: 18,
    },
    dominantColor: '#D97706',
    tracks: [
      { id: 't99', number: 1, title: 'Homebound', duration: '3:48', bpm: 105, key: 'G Major', energy: 58 },
      { id: 't100', number: 2, title: 'Dusty Trails', duration: '4:15', bpm: 98, key: 'D Major', energy: 52 },
      { id: 't101', number: 3, title: 'Whiskey Sunrise', duration: '4:32', bpm: 92, key: 'A Major', energy: 48 },
      { id: 't102', number: 4, title: 'Mountain High', duration: '3:55', bpm: 110, key: 'C Major', energy: 65 },
      { id: 't103', number: 5, title: 'Southern Comfort', duration: '4:28', bpm: 88, key: 'E Major', energy: 45 },
      { id: 't104', number: 6, title: 'Backwoods Blues', duration: '5:12', bpm: 85, key: 'F Major', energy: 42 },
      { id: 't105', number: 7, title: 'Lonesome Highway', duration: '4:45', bpm: 95, key: 'Bb Major', energy: 55 },
      { id: 't106', number: 8, title: 'Rodeo Dreams', duration: '3:38', bpm: 112, key: 'Ab Major', energy: 68 },
      { id: 't107', number: 9, title: 'Take Me Home', duration: '4:51', bpm: 90, key: 'Eb Major', energy: 62 },
    ],
  },
  {
    id: '14',
    title: 'Disco Nights',
    artist: 'Funkadelic Groove',
    year: '2029',
    genre: ['Disco', 'Funk'],
    cover: 'https://images.unsplash.com/photo-1571766752116-63b1e6514b53?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxkaXNjbyUyMGFsYnVtJTIwdmlueWwlMjBjb2xvcmZ1bHxlbnwxfHx8fDE3NzI2Mzc5NjJ8MA&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '46:35',
    mood: {
      energetic: 92,
      melancholic: 15,
      uplifting: 95,
      aggressive: 22,
    },
    dominantColor: '#A855F7',
    tracks: [
      { id: 't108', number: 1, title: 'Boogie Wonderland', duration: '4:52', bpm: 122, key: 'F Major', energy: 90 },
      { id: 't109', number: 2, title: 'Funky Town Fever', duration: '5:15', bpm: 118, key: 'C Major', energy: 92 },
      { id: 't110', number: 3, title: 'Dance Revolution', duration: '4:38', bpm: 124, key: 'G Major', energy: 88 },
      { id: 't111', number: 4, title: 'Disco Inferno', duration: '5:28', bpm: 120, key: 'D Major', energy: 95 },
      { id: 't112', number: 5, title: 'Saturday Night Groove', duration: '4:45', bpm: 126, key: 'A Major', energy: 86 },
      { id: 't113', number: 6, title: 'Glitter & Gold', duration: '5:02', bpm: 116, key: 'E Major', energy: 82 },
      { id: 't114', number: 7, title: 'Stayin\' Alive 2.0', duration: '4:32', bpm: 104, key: 'Bb Major', energy: 85 },
      { id: 't115', number: 8, title: 'Funky Drummer', duration: '5:48', bpm: 128, key: 'Ab Major', energy: 92 },
      { id: 't116', number: 9, title: 'Last Dance', duration: '6:15', bpm: 114, key: 'Eb Major', energy: 88 },
    ],
  },
  {
    id: '15',
    title: 'Quantum Beats',
    artist: 'Digital Future',
    year: '2030',
    genre: ['Electronic', 'Experimental'],
    cover: 'https://images.unsplash.com/photo-1703115015343-81b498a8c080?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxhbGJ1bSUyMGNvdmVyJTIwZWxlY3Ryb25pYyUyMG11c2ljfGVufDF8fHx8MTc3MjYzNzk1Nnww&ixlib=rb-4.1.0&q=80&w=1080&utm_source=figma&utm_medium=referral',
    duration: '55:18',
    mood: {
      energetic: 75,
      melancholic: 42,
      uplifting: 68,
      aggressive: 55,
    },
    dominantColor: '#06B6D4',
    tracks: [
      { id: 't117', number: 1, title: 'Neural Networks', duration: '6:28', bpm: 135, key: 'C# Minor', energy: 78 },
      { id: 't118', number: 2, title: 'Quantum Leap', duration: '7:15', bpm: 128, key: 'F# Minor', energy: 72 },
      { id: 't119', number: 3, title: 'Binary Stars', duration: '5:48', bpm: 142, key: 'B Minor', energy: 82 },
      { id: 't120', number: 4, title: 'Holographic Dreams', duration: '6:32', bpm: 130, key: 'E Minor', energy: 68 },
      { id: 't121', number: 5, title: 'Cybernetic Soul', duration: '7:05', bpm: 125, key: 'A Minor', energy: 75 },
      { id: 't122', number: 6, title: 'Digital Consciousness', duration: '8:18', bpm: 138, key: 'D Minor', energy: 80 },
      { id: 't123', number: 7, title: 'Nanotech Symphony', duration: '6:45', bpm: 132, key: 'G Minor', energy: 76 },
      { id: 't124', number: 8, title: 'Future Memories', duration: '7:07', bpm: 120, key: 'C Minor', energy: 65 },
    ],
  },
];
