import { useState } from "react";
import { MovieGrid } from "./components/MovieGrid";
import { MovieInfoPage } from "./components/MovieInfoPage";
import { MediaPlayer } from "./components/MediaPlayer";

interface Movie {
  id: number;
  title: string;
  posterUrl: string;
  backdropUrl: string;
  rating: number;
  aiRating: number;
  year: string;
  runtime: string;
  genres: string[];
  description: string;
  director: string;
  actors: {
    name: string;
    character: string;
    imageUrl: string;
  }[];
  progress?: number;
}

type Page = "grid" | "info" | "player";

export default function App() {
  const [currentPage, setCurrentPage] = useState<Page>("grid");
  const [selectedMovie, setSelectedMovie] =
    useState<Movie | null>(null);
  const [selectedSource, setSelectedSource] =
    useState<any>(null);

  const movies: Movie[] = [
    {
      id: 1,
      title: "Cyberpunk 2077: Edgerunners",
      posterUrl:
        "https://images.unsplash.com/photo-1536440136628-849c177e76a1?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1440404653325-ab127d49abc1?w=1920&h=1080&fit=crop",
      rating: 9.3,
      aiRating: 9.5,
      year: "2024",
      runtime: "2h 15m",
      genres: ["Sci-Fi", "Action", "Thriller", "Cyberpunk"],
      description:
        "In a dystopian future where technology and humanity collide, a young street kid tries to survive in a city obsessed with body modification and power. An epic journey through the neon-lit streets of Night City.",
      director: "Christopher Nolan",
      actors: [
        {
          name: "John Doe",
          character: "David Martinez",
          imageUrl:
            "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?w=200&h=200&fit=crop",
        },
        {
          name: "Jane Smith",
          character: "Lucy",
          imageUrl:
            "https://images.unsplash.com/photo-1494790108377-be9c29b29330?w=200&h=200&fit=crop",
        },
        {
          name: "Mike Johnson",
          character: "Maine",
          imageUrl:
            "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=200&h=200&fit=crop",
        },
        {
          name: "Sarah Williams",
          character: "Rebecca",
          imageUrl:
            "https://images.unsplash.com/photo-1438761681033-6461ffad8d80?w=200&h=200&fit=crop",
        },
      ],
      progress: 45,
    },
    {
      id: 2,
      title: "Dune: Part Two",
      posterUrl:
        "https://images.unsplash.com/photo-1594908900066-3f47337549d8?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1509773896068-7fd415d91e2e?w=1920&h=1080&fit=crop",
      rating: 9.2,
      aiRating: 9.4,
      year: "2024",
      runtime: "2h 46m",
      genres: ["Sci-Fi", "Adventure", "Drama"],
      description:
        "Paul Atreides unites with Chani and the Fremen while seeking revenge against the conspirators who destroyed his family.",
      director: "Denis Villeneuve",
      actors: [
        {
          name: "Timothée Chalamet",
          character: "Paul Atreides",
          imageUrl:
            "https://images.unsplash.com/photo-1506794778202-cad84cf45f1d?w=200&h=200&fit=crop",
        },
        {
          name: "Zendaya",
          character: "Chani",
          imageUrl:
            "https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=200&h=200&fit=crop",
        },
      ],
    },
    {
      id: 3,
      title: "Blade Runner 2049",
      posterUrl:
        "https://images.unsplash.com/photo-1478720568477-152d9b164e26?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1518709594023-6eab9bab7b23?w=1920&h=1080&fit=crop",
      rating: 8.9,
      aiRating: 9.1,
      year: "2017",
      runtime: "2h 44m",
      genres: ["Sci-Fi", "Thriller", "Mystery"],
      description:
        "A young blade runner discovers a long-buried secret that has the potential to plunge what's left of society into chaos.",
      director: "Denis Villeneuve",
      actors: [
        {
          name: "Ryan Gosling",
          character: "K",
          imageUrl:
            "https://images.unsplash.com/photo-1472099645785-5658abf4ff4e?w=200&h=200&fit=crop",
        },
        {
          name: "Harrison Ford",
          character: "Rick Deckard",
          imageUrl:
            "https://images.unsplash.com/photo-1519085360753-af0119f7cbe7?w=200&h=200&fit=crop",
        },
      ],
    },
    {
      id: 4,
      title: "Interstellar",
      posterUrl:
        "https://images.unsplash.com/photo-1419242902214-272b3f66ee7a?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1446776653964-20c1d3a81b06?w=1920&h=1080&fit=crop",
      rating: 9.0,
      aiRating: 9.3,
      year: "2014",
      runtime: "2h 49m",
      genres: ["Sci-Fi", "Drama", "Adventure"],
      description:
        "A team of explorers travel through a wormhole in space in an attempt to ensure humanity's survival.",
      director: "Christopher Nolan",
      actors: [
        {
          name: "Matthew McConaughey",
          character: "Cooper",
          imageUrl:
            "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?w=200&h=200&fit=crop",
        },
        {
          name: "Anne Hathaway",
          character: "Brand",
          imageUrl:
            "https://images.unsplash.com/photo-1544005313-94ddf0286df2?w=200&h=200&fit=crop",
        },
      ],
    },
    {
      id: 5,
      title: "The Matrix",
      posterUrl:
        "https://images.unsplash.com/photo-1626814026160-2237a95fc5a0?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1451187580459-43490279c0fa?w=1920&h=1080&fit=crop",
      rating: 8.7,
      aiRating: 8.9,
      year: "1999",
      runtime: "2h 16m",
      genres: ["Sci-Fi", "Action"],
      description:
        "A computer hacker learns from mysterious rebels about the true nature of his reality and his role in the war against its controllers.",
      director: "The Wachowskis",
      actors: [
        {
          name: "Keanu Reeves",
          character: "Neo",
          imageUrl:
            "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=200&h=200&fit=crop",
        },
        {
          name: "Laurence Fishburne",
          character: "Morpheus",
          imageUrl:
            "https://images.unsplash.com/photo-1506794778202-cad84cf45f1d?w=200&h=200&fit=crop",
        },
      ],
    },
    {
      id: 6,
      title: "Inception",
      posterUrl:
        "https://images.unsplash.com/photo-1616530940355-351fabd9524b?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1485163819542-13adeb5e0068?w=1920&h=1080&fit=crop",
      rating: 8.8,
      aiRating: 9.0,
      year: "2010",
      runtime: "2h 28m",
      genres: ["Sci-Fi", "Thriller", "Action"],
      description:
        "A thief who steals corporate secrets through the use of dream-sharing technology is given the inverse task of planting an idea.",
      director: "Christopher Nolan",
      actors: [
        {
          name: "Leonardo DiCaprio",
          character: "Cobb",
          imageUrl:
            "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?w=200&h=200&fit=crop",
        },
      ],
    },
    {
      id: 7,
      title: "Stranger Things",
      posterUrl:
        "https://images.unsplash.com/photo-1594909122845-11baa439b7bf?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1518709594023-6eab9bab7b23?w=1920&h=1080&fit=crop",
      rating: 8.8,
      aiRating: 8.7,
      year: "2016",
      runtime: "51m per episode",
      genres: ["Sci-Fi", "Horror", "Drama"],
      description:
        "When a young boy disappears, his mother, a police chief and his friends must confront terrifying supernatural forces.",
      director: "The Duffer Brothers",
      actors: [],
      progress: 62,
    },
    {
      id: 8,
      title: "The Last of Us",
      posterUrl:
        "https://images.unsplash.com/photo-1518676590629-3dcbd9c5a5c9?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1509773896068-7fd415d91e2e?w=1920&h=1080&fit=crop",
      rating: 9.1,
      aiRating: 9.2,
      year: "2023",
      runtime: "60m per episode",
      genres: ["Drama", "Action", "Sci-Fi"],
      description:
        "After a global pandemic destroys civilization, a hardened survivor takes charge of a 14-year-old girl.",
      director: "Craig Mazin",
      actors: [],
      progress: 34,
    },
    {
      id: 9,
      title: "Avatar",
      posterUrl:
        "https://images.unsplash.com/photo-1682687220742-aba13b6e50ba?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1464802686167-b939a6910659?w=1920&h=1080&fit=crop",
      rating: 8.5,
      aiRating: 8.6,
      year: "2009",
      runtime: "2h 42m",
      genres: ["Sci-Fi", "Adventure", "Fantasy"],
      description:
        "A paraplegic Marine dispatched to the moon Pandora on a unique mission becomes torn between following his orders and protecting the world he feels is his home.",
      director: "James Cameron",
      actors: [],
    },
    {
      id: 10,
      title: "Game of Thrones",
      posterUrl:
        "https://images.unsplash.com/photo-1489599849927-2ee91cede3ba?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1519681393784-d120267933ba?w=1920&h=1080&fit=crop",
      rating: 9.2,
      aiRating: 9.0,
      year: "2011",
      runtime: "57m per episode",
      genres: ["Fantasy", "Drama", "Adventure"],
      description:
        "Nine noble families fight for control over the lands of Westeros, while an ancient enemy returns.",
      director: "David Benioff",
      actors: [],
    },
    {
      id: 11,
      title: "Arcane",
      posterUrl:
        "https://images.unsplash.com/photo-1618945524163-32451704cbb8?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1478720568477-152d9b164e26?w=1920&h=1080&fit=crop",
      rating: 9.0,
      aiRating: 9.1,
      year: "2021",
      runtime: "40m per episode",
      genres: ["Animation", "Action", "Adventure"],
      description:
        "Set in utopian Piltover and the oppressed underground of Zaun, the story follows the origins of two iconic League champions.",
      director: "Christian Linke",
      actors: [],
    },
    {
      id: 12,
      title: "The Witcher",
      posterUrl:
        "https://images.unsplash.com/photo-1578632292335-df3abbb0d586?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1506905925346-21bda4d32df4?w=1920&h=1080&fit=crop",
      rating: 8.2,
      aiRating: 8.4,
      year: "2019",
      runtime: "60m per episode",
      genres: ["Fantasy", "Action", "Drama"],
      description:
        "Geralt of Rivia, a solitary monster hunter, struggles to find his place in a world where people often prove more wicked than beasts.",
      director: "Lauren Schmidt Hissrich",
      actors: [],
    },
    {
      id: 13,
      title: "Ex Machina",
      posterUrl:
        "https://images.unsplash.com/photo-1535016120720-40c646be5580?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1550745165-9bc0b252726f?w=1920&h=1080&fit=crop",
      rating: 7.7,
      aiRating: 8.2,
      year: "2014",
      runtime: "1h 48m",
      genres: ["Sci-Fi", "Drama", "Thriller"],
      description:
        "A young programmer is selected to participate in a groundbreaking experiment in synthetic intelligence.",
      director: "Alex Garland",
      actors: [],
    },
    {
      id: 14,
      title: "Arrival",
      posterUrl:
        "https://images.unsplash.com/photo-1564951434112-64d74cc2a2d7?w=300&h=450&fit=crop",
      backdropUrl:
        "https://images.unsplash.com/photo-1419242902214-272b3f66ee7a?w=1920&h=1080&fit=crop",
      rating: 7.9,
      aiRating: 8.5,
      year: "2016",
      runtime: "1h 56m",
      genres: ["Sci-Fi", "Drama", "Mystery"],
      description:
        "A linguist works with the military to communicate with alien lifeforms after twelve mysterious spacecraft appear around the world.",
      director: "Denis Villeneuve",
      actors: [],
    },
  ];

  const streamSources = [
    {
      sourceName: "Premium UHD Source",
      quality: "4K HDR Dolby Vision",
      fileSize: "15.2 GB",
      audioLanguage: "English (Atmos)",
      subtitles: ["English", "Spanish", "French", "German"],
      seederCount: 124,
    },
    {
      sourceName: "High Quality Source",
      quality: "1080p",
      fileSize: "4.8 GB",
      audioLanguage: "English (5.1)",
      subtitles: ["English", "Spanish"],
      seederCount: 89,
    },
    {
      sourceName: "Standard Source",
      quality: "720p",
      fileSize: "2.1 GB",
      audioLanguage: "English (Stereo)",
      subtitles: ["English"],
      seederCount: 34,
    },
  ];

  const handleMovieClick = (movie: Movie) => {
    setSelectedMovie(movie);
    setCurrentPage("info");
  };

  const handlePlay = (source: any) => {
    setSelectedSource(source);
    setCurrentPage("player");
  };

  const handleBack = () => {
    if (currentPage === "player") {
      setCurrentPage("info");
    } else {
      setCurrentPage("grid");
      setSelectedMovie(null);
    }
  };

  return (
    <div className="size-full bg-gradient-to-br from-slate-950 via-slate-900 to-slate-950 overflow-auto">
      {currentPage === "grid" && (
        <MovieGrid
          movies={movies}
          onMovieClick={handleMovieClick}
        />
      )}

      {currentPage === "info" && selectedMovie && (
        <MovieInfoPage
          backdropUrl={selectedMovie.backdropUrl}
          posterUrl={selectedMovie.posterUrl}
          title={selectedMovie.title}
          year={selectedMovie.year}
          runtime={selectedMovie.runtime}
          rating={selectedMovie.rating}
          aiRating={selectedMovie.aiRating}
          genres={selectedMovie.genres}
          description={selectedMovie.description}
          director={selectedMovie.director}
          actors={selectedMovie.actors}
          streamSources={streamSources}
          onBack={handleBack}
          onPlay={handlePlay}
          onPlayTrailer={() => console.log("Play trailer")}
        />
      )}

      {currentPage === "player" && selectedMovie && (
        <MediaPlayer
          videoUrl="https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"
          title={selectedMovie.title}
          onBack={handleBack}
        />
      )}
    </div>
  );
}