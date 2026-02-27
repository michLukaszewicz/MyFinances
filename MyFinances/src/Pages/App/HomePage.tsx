import { startTransition, useEffect, useOptimistic, useState } from "react";
import BalanceChart from "../../Components/Charts/BalanceChart/BalanceChart";
import CategoryChart from "../../Components/Charts/CategoryChart/CategoryChart";
import TransactionsPanel from "../../Components/HistoryPanel/TransactionsPanel";
import PageContent from "../../Components/PageContent/PageContent";
import type { Transaction } from "../../Models/Transaction";
import Box from "../../Components/Box/Box";
import TransactionService from "../../Services/ApiServices/TransactionService";
import type { ClientTransaction } from "../../Models/Dtos/TransactionClientDto";

const HomePage = () => {
  const [history, setHistory] = useState<Transaction[]>([]);

  const [optimisticHistory, updateOptimisticHistory] = useOptimistic(
    history as ClientTransaction[],
    (state: ClientTransaction[], transaction: ClientTransaction) => {
      const index = state.findIndex((t) => t.clientId === transaction.clientId);
      if (index === -1) return [...state, transaction];
      return state.map((t) => (t.clientId === transaction.clientId ? transaction : t));
    },
  );

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

  const onAddTransaction = async (form: Transaction) => {
    const optimisticTransaction = { ...form, id: -1, clientId: crypto.randomUUID(), date: new Date(form.date) };
    startTransition(async () => {
      updateOptimisticHistory(optimisticTransaction);
      try {
        const realId = await TransactionService.postTransaction(optimisticTransaction);
        setHistory((prev) => [...prev, { ...optimisticTransaction, id: realId }]);
      } catch {
        updateOptimisticHistory({ ...optimisticTransaction, id: -1 });
      }
    });
  };

  const onDeleteTransaction = async (id: number) => {
    if (confirm("Please confirm to delete the transaction.")) {
      startTransition(async () => {
        try {
          await TransactionService.deleteTransaction(id);
          setHistory((prev) => prev.filter((transaction) => transaction.id !== id));
        } catch {
          //add warning display
        }
      });
    }
  };

  return (
    <PageContent>
      <Box header="">
        <h1 className="text-4xl text-blue-500 font-bold text-center">Welcome!</h1>
        <div className="text-center font-semibold text-2xl my-10">
          <p>Manage your finances like a boss</p>
          <p className="mt-2">Check our basic features on sample data below or create a free account and test all the possibilities in real!</p>
          <div className="my-6" />
          <div className="space-x-2">
            <a href="/register" className="inline-block px-4 py-2 mx-2 bg-blue-500 text-white rounded-md hover:bg-blue-600 transition-colors">
              Register
            </a>
            <span>or</span>
            <a href="/login" className="inline-block px-4 py-2 mx-2 bg-blue-500 text-white rounded-md hover:bg-blue-600 transition-colors">
              Login
            </a>
          </div>
        </div>
      </Box>
      <BalanceChart history={optimisticHistory} />
      <CategoryChart history={optimisticHistory} />
      <TransactionsPanel transactionHistory={optimisticHistory} onAddTransaction={onAddTransaction} onDeleteTransaction={onDeleteTransaction} />
    </PageContent>
  );
};

export default HomePage;
