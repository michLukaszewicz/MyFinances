import { useEffect, useState } from "react";
import BalanceChart from "../../Components/Charts/BalanceChart/BalanceChart";
import CategoryChart from "../../Components/Charts/CategoryChart/CategoryChart";
import HistoryPanel from "../../Components/HistoryPanel/HistoryPanel";
import PageContent from "../../Components/PageContent/PageContent";
import type { Transaction } from "../../Models/Transaction";
import Box from "../../Components/Box/Box";
import TransactionService from "../../Services/ApiServices/TransactionService";
import AddTransaction from "../../Forms/Transactions/AddTransactionForm";

const HomePage = () => {
  const [history, setHistory] = useState<Transaction[]>([]);

  useEffect(() => {
    const fetchData = async () => {
      try {
        const response: Transaction[] = await TransactionService.getTestData();
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
        <div className="text-center font-semibold text-2xl my-10">
          <p>Manage your finances like a boss</p>
          <p className="mt-2">Check our basic features on sample data below or create a free account and test all the possibilities in real!</p>
          <div className="my-6" />
          <div className="space-x-2">
            <a href="/register" className="inline-block px-4 py-2 mx-2 bg-blue-500 text-white rounded-md hover:bg-blue-600 transition-colors">Register</a>
            <span>or</span>
            <a href="/login" className="inline-block px-4 py-2 mx-2 bg-blue-500 text-white rounded-md hover:bg-blue-600 transition-colors">Login</a>
          </div>
        </div>
      </Box>
      <BalanceChart history={history} />
      <CategoryChart history={history} />
      <AddTransaction />
      <HistoryPanel history={history} />
    </PageContent>
  );
};

export default HomePage;
