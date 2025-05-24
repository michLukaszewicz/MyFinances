import { useEffect, useState } from "react";
import BalanceChart from "../Components/Charts/BalanceChart/BalanceChart";
import CategoryChart from "../Components/Charts/CategoryChart/CategoryChart";
import HistoryPanel from "../Components/HistoryPanel/HistoryPanel";
import PageContent from "../Components/PageContent/PageContent";
import type { Transaction } from "../Models/Transaction";
import transactionService from "../Services/Api/transactionService";
import Box from "../Components/Box/Box";

const HomePage = () => {
  const [history, setHistory] = useState<Transaction[]>([]);

  useEffect(() => {
    const fetchData = async () => {
      try {
        const response: Transaction[] = await transactionService.getTestData();
        setHistory(response);
      } catch (error) {
        console.error("Error fetching transactions:", error);
      }
    };

    fetchData();
  }, []);

  return (
    <PageContent>
      <Box header="">
        <h1 className="text-4xl text-blue-500 font-bold text-center">Welcome!</h1>
        <p className="text-center font-semibold text-2xl my-10">Manage your finances like a boss <br />
        <span>Check our basic features on sample data below or create a free account and test all the possibilities in real!</span>
        </p>
      </Box>
      <BalanceChart history={history} />
      <CategoryChart history={history} />
      <HistoryPanel history={history} />
    </PageContent>
  );
};

export default HomePage;
