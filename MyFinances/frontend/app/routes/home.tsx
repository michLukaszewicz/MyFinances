import { useEffect, useState } from "react";
import type { Route } from "./+types/home";
import { Welcome } from "../welcome/welcome";
import { apiFetch } from "../lib/api";

export function meta({}: Route.MetaArgs) {
  return [
    { title: "New React Router App" },
    { name: "description", content: "Welcome to React Router!" },
  ];
}

interface WeatherForecast {
  date: string;
  temperatureC: number;
  summary: string | null;
  temperatureF: number;
}

function ApiStatus() {
  const [forecast, setForecast] = useState<WeatherForecast[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    apiFetch<WeatherForecast[]>("/weatherforecast")
      .then(setForecast)
      .catch((err: Error) => setError(err.message));
  }, []);

  if (error) {
    return (
      <p className="text-center text-sm text-red-600">
        API call failed: {error}
      </p>
    );
  }

  if (!forecast) {
    return <p className="text-center text-sm text-gray-500">Calling API…</p>;
  }

  return (
    <p className="text-center text-sm text-green-600">
      API reachable — first forecast: {forecast[0].summary} (
      {forecast[0].temperatureC}°C)
    </p>
  );
}

export default function Home() {
  return (
    <>
      <ApiStatus />
      <Welcome />
    </>
  );
}
